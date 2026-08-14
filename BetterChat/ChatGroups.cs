using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterChatHarmony
{
    public class ChatRenderOutput
    {
        public string Chat;
        public string Console;
        public string Username;
        public string Message;
        public string Color;
    }

    public class ChatGroup
    {
        public string GroupName;
        public int Priority = 0;
        public TitleSettings Title = new TitleSettings();
        public UsernameSettings Username = new UsernameSettings();
        public MessageSettings Message = new MessageSettings();
        public FormatSettings Format = new FormatSettings();

        public ChatGroup() { }

        public ChatGroup(string name)
        {
            GroupName = name;
            Title = new TitleSettings(name);
        }

        public class TitleSettings
        {
            public string Text = "[Player]";
            public string Color = "#55aaff";
            public int Size = 15;
            public bool Hidden = false;
            public bool HiddenIfNotPrimary = false;
            /// <summary>Glue this title to the name with no space ([VIP]✓Name). Does not count toward Maximal Titles.</summary>
            public bool AttachToUsername = false;
            public bool Bold = false;

            public TitleSettings() { }

            public TitleSettings(string groupName)
            {
                if (groupName != "default" && groupName != null)
                    Text = "[" + groupName + "]";
            }

            public string GetUniversalColor() => StripHash(Color);
        }

        public class UsernameSettings
        {
            public string Color = "#55aaff";
            public int Size = 15;
            public string GetUniversalColor() => StripHash(Color);
        }

        public class MessageSettings
        {
            public string Color = "white";
            public int Size = 15;
            public string GetUniversalColor() => StripHash(Color);
        }

        public class FormatSettings
        {
            public string Chat = "{Title} {Username}: {Message}";
            public string Console = "{Title} {Username}: {Message}";
        }

        public static string StripHash(string color)
        {
            if (string.IsNullOrEmpty(color)) return "white";
            return color[0] == '#' ? color.Substring(1) : color;
        }
    }

    public class BetterChatMessage
    {
        public BasePlayer Player;
        public string Username;
        public string Message;
        public List<string> Titles = new List<string>();
        public string NamePrefix = "";
        public string PrimaryGroup;
        public ChatGroup.UsernameSettings UsernameSettings;
        public ChatGroup.MessageSettings MessageSettings;
        public ChatGroup.FormatSettings FormatSettings;
        public List<string> BlockedReceivers = new List<string>();
        public CancelOptions CancelOption;

        public enum CancelOptions
        {
            None = 0,
            BetterChatOnly = 1,
            BetterChatAndDefault = 2
        }

        public ChatRenderOutput GetOutput()
        {
            var username = Username ?? "";
            var message = Message ?? "";

            if (message.IndexOf("[#", StringComparison.Ordinal) >= 0 || message.IndexOf("[+", StringComparison.Ordinal) >= 0)
                message = message.Replace("[", string.Empty).Replace("]", string.Empty);
            if (username.IndexOf("[#", StringComparison.Ordinal) >= 0 || username.IndexOf("[+", StringComparison.Ordinal) >= 0)
                username = username.Replace("[", string.Empty).Replace("]", string.Empty);

            string titleJoin = JoinTitles(Titles);
            string attached = NamePrefix ?? "";
            bool nameAlreadyRich = username.IndexOf("<color", StringComparison.OrdinalIgnoreCase) >= 0;
            string usernameFmt = attached + (nameAlreadyRich
                ? username
                : "[#" + UsernameSettings.GetUniversalColor() + "][+" + UsernameSettings.Size + "]" + username + "[/+][/#]");
            bool messageAlreadyRich = message.IndexOf("<color", StringComparison.OrdinalIgnoreCase) >= 0;
            string messageFmt = messageAlreadyRich
                ? message
                : "[#" + MessageSettings.GetUniversalColor() + "][+" + MessageSettings.Size + "]" + message + "[/+][/#]";

            // chat.add2 username is displayed as-is; vanilla EscapeRichText-strips tags, and
            // <size>/<color> in the name field often disappear. Titles must be plain text.
            string plainTitles = ChatFormatter.StripRichText(ChatFormatter.ToUnityRichText(titleJoin)).Trim();
            string plainAttached = ChatFormatter.StripRichText(ChatFormatter.ToUnityRichText(attached)).Trim();
            string plainName = ChatFormatter.StripRichText(username);
            string add2Name = (string.IsNullOrEmpty(plainTitles) ? "" : plainTitles + (string.IsNullOrEmpty(plainAttached) ? " " : ""))
                              + plainAttached + plainName;
            string color = UsernameSettings?.Color ?? "#55aaff";
            if (!string.IsNullOrEmpty(color) && color[0] != '#' && color.IndexOf('<') < 0)
                color = "#" + color;

            string chatFormat = FormatSettings.Chat ?? "{Title} {Username}: {Message}";
            string consoleFormat = FormatSettings.Console ?? "{Title} {Username}: {Message}";
            if (!string.IsNullOrEmpty(attached))
            {
                chatFormat = chatFormat.Replace("{Title} {Username}", "{Title}{Username}");
                consoleFormat = consoleFormat.Replace("{Title} {Username}", "{Title}{Username}");
            }

            var output = new ChatRenderOutput
            {
                Username = add2Name.Trim(),
                Message = ChatFormatter.ToUnityRichText(messageFmt),
                Color = color,
                Console = ChatFormatter.StripRichText(ReplaceTokens(consoleFormat, titleJoin, usernameFmt, messageFmt)).Trim()
            };
            string chatLine = ReplaceTokens(chatFormat, titleJoin, usernameFmt, messageFmt);
            output.Chat = ChatFormatter.ToUnityRichText(chatLine).Trim();
            while (output.Chat.IndexOf("  ", StringComparison.Ordinal) >= 0)
                output.Chat = output.Chat.Replace("  ", " ");
            return output;
        }

        private string ReplaceTokens(string format, string title, string username, string message)
        {
            var sb = new StringBuilder(format);
            sb.Replace("{Title}", title);
            sb.Replace("{Username}", username);
            sb.Replace("{Group}", PrimaryGroup ?? "");
            sb.Replace("{Message}", message);
            sb.Replace("{ID}", Player != null ? Player.UserIDString : "");
            sb.Replace("{Time}", DateTime.Now.TimeOfDay.ToString());
            sb.Replace("{Date}", DateTime.Now.ToString());
            return sb.ToString();
        }

        private static string JoinTitles(List<string> titles)
        {
            if (titles == null || titles.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < titles.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(titles[i]);
            }
            return sb.ToString();
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["Player"] = Player,
                ["Message"] = Message,
                ["Username"] = Username,
                ["Titles"] = Titles,
                ["NamePrefix"] = NamePrefix ?? "",
                ["PrimaryGroup"] = PrimaryGroup,
                ["BlockedReceivers"] = BlockedReceivers,
                ["UsernameSettings"] = new Dictionary<string, object>
                {
                    ["Color"] = UsernameSettings.Color,
                    ["Size"] = UsernameSettings.Size
                },
                ["MessageSettings"] = new Dictionary<string, object>
                {
                    ["Color"] = MessageSettings.Color,
                    ["Size"] = MessageSettings.Size
                },
                ["FormatSettings"] = new Dictionary<string, object>
                {
                    ["Chat"] = FormatSettings.Chat,
                    ["Console"] = FormatSettings.Console
                },
                ["CancelOption"] = CancelOption
            };
        }

        public static BetterChatMessage FromDictionary(Dictionary<string, object> dictionary)
        {
            var usernameSettings = dictionary["UsernameSettings"] as Dictionary<string, object>;
            var messageSettings = dictionary["MessageSettings"] as Dictionary<string, object>;
            var formatSettings = dictionary["FormatSettings"] as Dictionary<string, object>;

            return new BetterChatMessage
            {
                Player = dictionary["Player"] as BasePlayer,
                Message = dictionary["Message"] as string,
                Username = dictionary["Username"] as string,
                Titles = dictionary["Titles"] as List<string> ?? new List<string>(),
                NamePrefix = dictionary.ContainsKey("NamePrefix") ? dictionary["NamePrefix"] as string ?? "" : "",
                PrimaryGroup = dictionary["PrimaryGroup"] as string,
                BlockedReceivers = dictionary["BlockedReceivers"] as List<string> ?? new List<string>(),
                UsernameSettings = new ChatGroup.UsernameSettings
                {
                    Color = usernameSettings != null ? usernameSettings["Color"] as string : "#55aaff",
                    Size = usernameSettings != null ? Convert.ToInt32(usernameSettings["Size"]) : 15
                },
                MessageSettings = new ChatGroup.MessageSettings
                {
                    Color = messageSettings != null ? messageSettings["Color"] as string : "white",
                    Size = messageSettings != null ? Convert.ToInt32(messageSettings["Size"]) : 15
                },
                FormatSettings = new ChatGroup.FormatSettings
                {
                    Chat = formatSettings != null ? formatSettings["Chat"] as string : "{Title} {Username}: {Message}",
                    Console = formatSettings != null ? formatSettings["Console"] as string : "{Title} {Username}: {Message}"
                },
                CancelOption = dictionary.ContainsKey("CancelOption")
                    ? (CancelOptions)Convert.ToInt32(dictionary["CancelOption"])
                    : CancelOptions.None
            };
        }
    }

    public static class ChatFormatter
    {
        private static readonly string[] StringReplacements =
        {
            "<b>", "</b>", "<i>", "</i>", "</size>", "</color>"
        };

        private static readonly Regex[] RegexReplacements =
        {
            new Regex(@"<voffset=(?:.|\s)*?>", RegexOptions.Compiled),
            new Regex(@"<color=.+?>", RegexOptions.Compiled),
            new Regex(@"<size=.+?>", RegexOptions.Compiled)
        };

        private static readonly Regex UniversalTag = new Regex(@"\[#([^\]]+)\]", RegexOptions.Compiled);
        private static readonly Regex SizeTag = new Regex(@"\[\+(\d+)\]", RegexOptions.Compiled);
        private static readonly Regex HexColor = new Regex(@"^[0-9A-Fa-f]{3,8}$", RegexOptions.Compiled);

        public static string StripRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            for (int i = 0; i < StringReplacements.Length; i++)
                text = text.Replace(StringReplacements[i], string.Empty);
            for (int i = 0; i < RegexReplacements.Length; i++)
                text = RegexReplacements[i].Replace(text, string.Empty);
            return UniversalTag.Replace(SizeTag.Replace(text.Replace("[/+]", "").Replace("[/#]", ""), ""), "");
        }

        public static string ToUnityRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = UniversalTag.Replace(text, m =>
            {
                var c = m.Groups[1].Value;
                if (HexColor.IsMatch(c))
                    return "<color=#" + c + ">";
                return "<color=" + c + ">";
            });
            text = SizeTag.Replace(text, "<size=$1>");
            text = text.Replace("[/+]", "</size>").Replace("[/#]", "</color>");
            return text;
        }

        public static string FormatTitle(ChatGroup.TitleSettings title)
        {
            string text = title.Text ?? "";
            if (title.Bold)
                text = "<b>" + text + "</b>";
            return "[#" + title.GetUniversalColor() + "][+" + title.Size + "]" + text + "[/+][/#]";
        }
    }

    public static class ChatGroupFields
    {
        public static readonly Dictionary<string, Field> Fields =
            new Dictionary<string, Field>(StringComparer.InvariantCultureIgnoreCase)
            {
                ["Priority"] = new Field(g => g.Priority, (g, v) => g.Priority = int.Parse(v), "number"),
                ["Title"] = new Field(g => g.Title.Text, (g, v) => g.Title.Text = v, "text"),
                ["TitleColor"] = new Field(g => g.Title.Color, (g, v) => g.Title.Color = v, "color"),
                ["TitleSize"] = new Field(g => g.Title.Size, (g, v) => g.Title.Size = int.Parse(v), "number"),
                ["TitleHidden"] = new Field(g => g.Title.Hidden, (g, v) => g.Title.Hidden = bool.Parse(v), "true/false"),
                ["TitleHiddenIfNotPrimary"] = new Field(g => g.Title.HiddenIfNotPrimary, (g, v) => g.Title.HiddenIfNotPrimary = bool.Parse(v), "true/false"),
                ["TitleAttachToUsername"] = new Field(g => g.Title.AttachToUsername, (g, v) => g.Title.AttachToUsername = bool.Parse(v), "true/false"),
                ["TitleBold"] = new Field(g => g.Title.Bold, (g, v) => g.Title.Bold = bool.Parse(v), "true/false"),
                ["UsernameColor"] = new Field(g => g.Username.Color, (g, v) => g.Username.Color = v, "color"),
                ["UsernameSize"] = new Field(g => g.Username.Size, (g, v) => g.Username.Size = int.Parse(v), "number"),
                ["MessageColor"] = new Field(g => g.Message.Color, (g, v) => g.Message.Color = v, "color"),
                ["MessageSize"] = new Field(g => g.Message.Size, (g, v) => g.Message.Size = int.Parse(v), "number"),
                ["ChatFormat"] = new Field(g => g.Format.Chat, (g, v) => g.Format.Chat = v, "text"),
                ["ConsoleFormat"] = new Field(g => g.Format.Console, (g, v) => g.Format.Console = v, "text")
            };

        public enum SetValueResult
        {
            Success,
            InvalidField,
            InvalidValue
        }

        public class Field
        {
            public Func<ChatGroup, object> Getter { get; }
            public Action<ChatGroup, string> Setter { get; }
            public string UserFriendlyType { get; }

            public Field(Func<ChatGroup, object> getter, Action<ChatGroup, string> setter, string userFriendlyType)
            {
                Getter = getter;
                Setter = setter;
                UserFriendlyType = userFriendlyType;
            }
        }

        public static SetValueResult SetField(ChatGroup group, string field, string value)
        {
            if (!Fields.TryGetValue(field, out var f))
                return SetValueResult.InvalidField;
            try { f.Setter(group, value); }
            catch (FormatException) { return SetValueResult.InvalidValue; }
            catch (OverflowException) { return SetValueResult.InvalidValue; }
            return SetValueResult.Success;
        }

        public static string FieldList()
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var kvp in Fields)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append('(').Append(kvp.Value.UserFriendlyType).Append(") ").Append(kvp.Key);
            }
            return sb.ToString();
        }
    }
}
