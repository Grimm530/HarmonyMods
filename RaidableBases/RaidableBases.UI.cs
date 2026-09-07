using Facepunch;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using Rust.Ai.Gen2;
using Rust.Ai.Gen2.Nav;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
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

        #region UI

        public enum UiType { Buyable, Cooldown, Delay, Lockout, PasteProgress, Status, Teleport, Invalid }

        public UiHandler UI = new();

        internal void LogUiMessage(string message) => Puts(message);

        public class Vector2Converter : JsonConverter
        {
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    string[] values = reader.Value?.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (values?.Length == 2 &&
                        float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                    {
                        return new Vector2(x, y);
                    }

                    return existingValue is Vector2 current ? current : Vector2.zero;
                }

                if (reader.TokenType == JsonToken.StartObject)
                {
                    JObject value = JObject.Load(reader);
                    return new Vector2(value.Value<float?>("x") ?? 0f, value.Value<float?>("y") ?? 0f);
                }

                return existingValue is Vector2 existing ? existing : Vector2.zero;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Vector2 vector = (Vector2)value;
                writer.WriteValue(FormattableString.Invariant($"{vector.x:0.######} {vector.y:0.######}"));
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector2);
            }
        }

        public class UiOffsets
        {
            [JsonProperty(PropertyName = "Offset Min")]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 Min { get; set; }

            [JsonProperty(PropertyName = "Offset Max")]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 Max { get; set; }

            [JsonProperty(PropertyName = "Normalized Anchor")]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 NormalizedAnchor { get; set; }

            public UiOffsets()
            {
                Min = Vector2.zero;
                Max = Vector2.zero;
            }

            public UiOffsets(Vector2 min, Vector2 max)
            {
                Min = min;
                Max = max;
            }

            public UiOffsets Clone()
            {
                return new(Min, Max) { NormalizedAnchor = NormalizedAnchor };
            }

            public bool Equals(UiOffsets other)
            {
                return other != null && other.Min == Min && other.Max == Max && other.NormalizedAnchor == NormalizedAnchor;
            }
            public void MoveLeft(float units)
            {
                Min -= new Vector2(units, 0);
                Max -= new Vector2(units, 0);
            }
            public void MoveRight(float units)
            {
                Min += new Vector2(units, 0);
                Max += new Vector2(units, 0);
            }
            public void MoveUp(float units)
            {
                Min += new Vector2(0, units);
                Max += new Vector2(0, units);
            }
            public void MoveDown(float units)
            {
                Min -= new Vector2(0, units);
                Max -= new Vector2(0, units);
            }
            internal float Left => Min.x;
            internal float Top => Min.y;
            internal float Right => Max.x;
            internal float Bottom => Max.y;
            internal string MinString => FormattableString.Invariant($"{Left:0.###} {Top:0.###}");
            internal string MaxString => FormattableString.Invariant($"{Right:0.###} {Bottom:0.###}");
        }

        public class UiHandler
        {
            private const int UI_DATA_VERSION = 3;
            private const int STATUS_LAYOUT_VERSION = 2;
            private const int PASTE_LAYOUT_VERSION = 1;
            private const int BUYABLE_LAYOUT_VERSION = 1;
            private const int COOLDOWN_LAYOUT_VERSION = 1;
            private const int DELAY_LAYOUT_VERSION = 2;
            private const int LOCKOUT_LAYOUT_VERSION = 1;
            private const int TELEPORT_LAYOUT_VERSION = 1;
            private const double STATUS_REFRESH_INTERVAL = 1d;
            private const float UI_DRAG_EPSILON = 0.002f;
            private const string STATUS_LAYOUT_CARDS = "Cards";
            private const string BUYABLE_UI = "RB_UI_Buyable";
            private const string COOLDOWN_UI = "RB_UI_Cooldown";
            private const string DELAY_UI = "RB_UI_Delay";
            private const string LOCKOUT_UI = "RB_UI_Lockout";
            private const string PASTE_PROGRESS_UI = "RB_UI_PasteProgress";
            private const string STATUS_UI = "RB_UI_Status";
            private const string TARGET_INFO_UI = "RB_UI_TargetInfo";
            private const string TELEPORT_UI = "RB_UI_Teleport";
            private const string CONFIG_IMPORT_UI = "RB_UI_ConfigImport";
            private const string STATUS_MODE_DOT = "RB_UI_Status_ModeDot";
            private const string STATUS_TITLE_TEXT = "RB_UI_Status_TitleText";
            private const string STATUS_SUBTITLE_TEXT = "RB_UI_Status_SubtitleText";
            private const string STATUS_TIMER_TEXT = "RB_UI_Status_TimerText";
            private const string STATUS_MOVE_BUTTON = "RB_UI_Status_MoveButton";
            private const string STATUS_MOVE_TEXT = "RB_UI_Status_MoveText";
            private const string STATUS_BODY = "RB_UI_Status_Body";
            private const string STATUS_PRIMARY_CARD = "RB_UI_Status_PrimaryCard";
            private const string STATUS_PRIMARY_LABEL = "RB_UI_Status_PrimaryLabel";
            private const string STATUS_PRIMARY_VALUE = "RB_UI_Status_PrimaryValue";
            private const string STATUS_LOOT_CARD = "RB_UI_Status_LootCard";
            private const string STATUS_LOOT_LABEL = "RB_UI_Status_LootLabel";
            private const string STATUS_LOOT_VALUE = "RB_UI_Status_LootValue";
            private const string PASTE_TITLE_TEXT = "RB_UI_PasteProgress_TitleText";
            private const string PASTE_FILENAME_TEXT = "RB_UI_PasteProgress_FilenameText";
            private const string PASTE_STAGE_TEXT = "RB_UI_PasteProgress_StageText";
            private const string PASTE_PERCENT_TEXT = "RB_UI_PasteProgress_PercentText";
            private const string PASTE_MOVE_BUTTON = "RB_UI_PasteProgress_MoveButton";
            private const string PASTE_MOVE_TEXT = "RB_UI_PasteProgress_MoveText";
            private const string PASTE_BODY = "RB_UI_PasteProgress_Body";
            private const string PASTE_TRACK = "RB_UI_PasteProgress_Track";
            private const string PASTE_FILL = "RB_UI_PasteProgress_Fill";
            private const string DELAY_TITLE_TEXT = "RB_UI_Delay_TitleText";
            private const string DELAY_TIME_TEXT = "RB_UI_Delay_TimeText";
            private const string DELAY_MOVE_BUTTON = "RB_UI_Delay_MoveButton";
            private const string DELAY_MOVE_TEXT = "RB_UI_Delay_MoveText";
            private const string TELEPORT_TIME_TEXT = "RB_UI_Teleport_TimeText";
            private const string TELEPORT_MOVE_BUTTON = "RB_UI_Teleport_MoveButton";
            private const string TELEPORT_MOVE_TEXT = "RB_UI_Teleport_MoveText";

            public string BUYABLE_PARENT = "Hud";
            public string COOLDOWN_PARENT = "Overlay";
            public string DELAY_PARENT = "Overlay";
            public string LOCKOUT_PARENT = "Overlay";
            public string STATUS_PARENT = "Overlay";
            public string ELEVATOR_PARENT = "Hud";
            public string TELEPORT_PARENT = "Hud";
            public RaidableBases Instance;
            public StoredData data => Instance.data;
            public Configuration config => Instance.config;

            public HashSet<ulong> PrivateEvents = new();
            public HashSet<ulong> PublicEvents = new();
            public Dictionary<ulong, TimeSettings> Teleport = new();
            public Dictionary<ulong, Dictionary<UiType, UiOffsets>> Offsets = new();
            public Dictionary<ulong, Dictionary<UiType, Timer>> Movers = new();
            private readonly Dictionary<ulong, PlayerUi> users = new();

            public UiOffsets DefaultBuyableOffsets, DefaulCooldownOffsets, DefaultDelayOffsets, DefaultLockoutOffsets, DefaultPasteProgressOffsets, DefaultStatusOffsets, DefaultTeleportOffsets;

            private Coroutine statusRefreshCoroutine;

            public struct UiPalette
            {
                internal string Background, Panel, Cell, Divider, ProgressBackground, Accent, Text, Muted;

                internal UiPalette(string background, string panel, string cell, string divider, string progressBackground, string accent, string text, string muted)
                {
                    Background = background;
                    Panel = panel;
                    Cell = cell;
                    Divider = divider;
                    ProgressBackground = progressBackground;
                    Accent = accent;
                    Text = text;
                    Muted = muted;
                }
            }

            internal struct PasteSnapshot
            {
                internal bool ShowFileName, ShowMoveButton, Moving;
                internal string FilenameText, StageText, PercentText, MoveText;
                internal float Progress;

                internal PasteSnapshot(bool showFileName, bool showMoveButton, bool moving, string filenameText, string stageText, string percentText, string moveText, float progress)
                {
                    ShowFileName = showFileName;
                    ShowMoveButton = showMoveButton;
                    Moving = moving;
                    FilenameText = filenameText;
                    StageText = stageText;
                    PercentText = percentText;
                    MoveText = moveText;
                    Progress = progress;
                }
            }

            internal struct StatusSnapshot
            {
                internal bool ShowLoot, UseRows, ShowMoveButton, Moving;
                internal string TitleText, SubtitleText, TimerText, MoveText, PrimaryLabel, PrimaryValue, LootLabel, LootValue, ModeColor, PrimaryColor;

                internal StatusSnapshot(bool showLoot, bool useRows, bool showMoveButton, bool moving, string titleText, string subtitleText, string timerText, string moveText, string primaryLabel, string primaryValue, string lootLabel, string lootValue, string modeColor, string primaryColor)
                {
                    ShowLoot = showLoot;
                    UseRows = useRows;
                    ShowMoveButton = showMoveButton;
                    Moving = moving;
                    TitleText = titleText;
                    SubtitleText = subtitleText;
                    TimerText = timerText;
                    MoveText = moveText;
                    PrimaryLabel = primaryLabel;
                    PrimaryValue = primaryValue;
                    LootLabel = lootLabel;
                    LootValue = lootValue;
                    ModeColor = modeColor;
                    PrimaryColor = primaryColor;
                }
            }

            private class UiDataFile
            {
                [JsonProperty(PropertyName = "Version")]
                public int Version;

                [JsonProperty(PropertyName = "Layout Versions", NullValueHandling = NullValueHandling.Ignore)]
                public UiLayoutVersions LayoutVersions;

                [JsonProperty(PropertyName = "Players")]
                public Dictionary<ulong, UiPlayerData> Players = new();
            }

            private class UiLayoutVersions
            {
                [JsonProperty(PropertyName = "Buyable UI")]
                public int Buyable;

                [JsonProperty(PropertyName = "Buyable Cooldowns UI")]
                public int Cooldown;

                [JsonProperty(PropertyName = "PVP Delay UI")]
                public int Delay;

                [JsonProperty(PropertyName = "Lockouts UI")]
                public int Lockout;

                [JsonProperty(PropertyName = "Paste Progress UI")]
                public int PasteProgress;

                [JsonProperty(PropertyName = "Status UI")]
                public int Status;

                [JsonProperty(PropertyName = "Buyable Teleport UI")]
                public int Teleport;

                internal bool IsCurrent => Buyable == BUYABLE_LAYOUT_VERSION && Cooldown == COOLDOWN_LAYOUT_VERSION && Delay == DELAY_LAYOUT_VERSION &&
                    Lockout == LOCKOUT_LAYOUT_VERSION && PasteProgress == PASTE_LAYOUT_VERSION && Status == STATUS_LAYOUT_VERSION && Teleport == TELEPORT_LAYOUT_VERSION;
            }

            private class UiPlayerData
            {
                [JsonProperty(PropertyName = "Buyable UI", NullValueHandling = NullValueHandling.Ignore)]
                public UiOffsets Buyable;

                [JsonProperty(PropertyName = "Buyable Cooldowns UI", NullValueHandling = NullValueHandling.Ignore)]
                public UiOffsets Cooldown;

                [JsonProperty(PropertyName = "PVP Delay UI", NullValueHandling = NullValueHandling.Ignore)]
                public UiOffsets Delay;

                [JsonProperty(PropertyName = "Lockouts UI", NullValueHandling = NullValueHandling.Ignore)]
                public UiOffsets Lockout;

                [JsonProperty(PropertyName = "Paste Progress UI", NullValueHandling = NullValueHandling.Ignore)]
                public UiOffsets PasteProgress;

                [JsonProperty(PropertyName = "Status UI", NullValueHandling = NullValueHandling.Ignore)]
                public UiOffsets Status;

                [JsonProperty(PropertyName = "Buyable Teleport UI", NullValueHandling = NullValueHandling.Ignore)]
                public UiOffsets Teleport;
            }

            private class PlayerUi
            {
                internal BasePlayer player;
                internal ulong userid;
                internal double Cooldown;
                internal double Delay;
                internal double Lockout;
                internal double Status;
                internal double TeleportRefresh;
                internal bool StatusUiCreated;
                internal bool StatusMoving;
                internal bool StatusShowLoot;
                internal bool StatusUseRows;
                internal bool StatusShowMoveButton;
                internal RaidableBase StatusRaid;
                internal Vector2 StatusOffsetMin;
                internal Vector2 StatusOffsetMax;
                internal Vector2 StatusNormalizedAnchor;
                internal bool HasSnapshot;
                internal StatusSnapshot Snapshot;
                internal bool PasteUiCreated;
                internal bool PasteMoving;
                internal bool PasteShowFileName;
                internal bool PasteShowMoveButton;
                internal Vector2 PasteOffsetMin;
                internal Vector2 PasteOffsetMax;
                internal Vector2 PasteNormalizedAnchor;
                internal bool HasPasteSnapshot;
                internal PasteSnapshot PasteSnapshot;
                internal bool DelayUiCreated;
                internal bool DelayMoving;
                internal string DelayTimeText;
                internal bool TeleportUiCreated;
                internal bool TeleportMoving;
                internal bool TeleportShowMoveButton;
                internal string TeleportMode;
                internal string TeleportTimeText;
                internal Vector2 TeleportOffsetMin;
                internal Vector2 TeleportOffsetMax;
                internal Vector2 TeleportNormalizedAnchor;

                internal bool HasRefresh => Cooldown > 0d || Delay > 0d || Lockout > 0d || Status > 0d || TeleportRefresh > 0d;
                internal bool IsConnected => player != null && player.IsConnected;

                internal bool IsSameLayout(UiOffsets offsets, bool showLoot, bool useRows, bool showMoveButton)
                {
                    return StatusUiCreated && StatusShowLoot == showLoot && StatusUseRows == useRows && StatusShowMoveButton == showMoveButton && StatusOffsetMin == offsets.Min && StatusOffsetMax == offsets.Max && StatusNormalizedAnchor == offsets.NormalizedAnchor;
                }

                internal void SetLayout(UiOffsets offsets, bool showLoot, bool useRows, bool showMoveButton)
                {
                    StatusShowLoot = showLoot;
                    StatusUseRows = useRows;
                    StatusShowMoveButton = showMoveButton;
                    StatusOffsetMin = offsets.Min;
                    StatusOffsetMax = offsets.Max;
                    StatusNormalizedAnchor = offsets.NormalizedAnchor;
                }

                internal void SetPosition(UiOffsets offsets)
                {
                    StatusOffsetMin = offsets.Min;
                    StatusOffsetMax = offsets.Max;
                    StatusNormalizedAnchor = offsets.NormalizedAnchor;
                }

                internal void SetSnapshot(StatusSnapshot value)
                {
                    Snapshot = value;
                    StatusMoving = value.Moving;
                    HasSnapshot = true;
                }

                internal void InvalidateStatus()
                {
                    StatusUiCreated = false;
                    StatusMoving = false;
                    StatusShowLoot = false;
                    StatusUseRows = false;
                    StatusShowMoveButton = false;
                    StatusOffsetMin = default;
                    StatusOffsetMax = default;
                    StatusNormalizedAnchor = default;
                    HasSnapshot = false;
                    Snapshot = default;
                }

                internal void ResetStatus()
                {
                    Status = 0d;
                    StatusRaid = null;
                    InvalidateStatus();
                }

                internal bool IsSamePasteLayout(UiOffsets offsets, bool showFileName, bool showMoveButton)
                {
                    return PasteUiCreated && PasteShowFileName == showFileName && PasteShowMoveButton == showMoveButton && PasteOffsetMin == offsets.Min && PasteOffsetMax == offsets.Max && PasteNormalizedAnchor == offsets.NormalizedAnchor;
                }

                internal void SetPasteLayout(UiOffsets offsets, bool showFileName, bool showMoveButton)
                {
                    PasteShowFileName = showFileName;
                    PasteShowMoveButton = showMoveButton;
                    SetPastePosition(offsets);
                }

                internal void SetPastePosition(UiOffsets offsets)
                {
                    PasteOffsetMin = offsets.Min;
                    PasteOffsetMax = offsets.Max;
                    PasteNormalizedAnchor = offsets.NormalizedAnchor;
                }

                internal void SetPasteSnapshot(PasteSnapshot value)
                {
                    PasteSnapshot = value;
                    PasteMoving = value.Moving;
                    HasPasteSnapshot = true;
                }

                internal void InvalidatePaste()
                {
                    PasteUiCreated = false;
                    PasteMoving = false;
                    PasteShowFileName = false;
                    PasteShowMoveButton = false;
                    PasteOffsetMin = default;
                    PasteOffsetMax = default;
                    PasteNormalizedAnchor = default;
                    HasPasteSnapshot = false;
                    PasteSnapshot = default;
                }

                internal void InvalidateDelay()
                {
                    DelayUiCreated = false;
                    DelayMoving = false;
                    DelayTimeText = null;
                }

                internal bool IsSameTeleportLayout(UiOffsets offsets, string mode, bool showMoveButton)
                {
                    return TeleportUiCreated && TeleportShowMoveButton == showMoveButton && TeleportMode == mode && TeleportOffsetMin == offsets.Min && TeleportOffsetMax == offsets.Max && TeleportNormalizedAnchor == offsets.NormalizedAnchor;
                }

                internal void SetTeleportLayout(UiOffsets offsets, string mode, bool showMoveButton)
                {
                    TeleportMode = mode;
                    TeleportShowMoveButton = showMoveButton;
                    SetTeleportPosition(offsets);
                }

                internal void SetTeleportPosition(UiOffsets offsets)
                {
                    TeleportOffsetMin = offsets.Min;
                    TeleportOffsetMax = offsets.Max;
                    TeleportNormalizedAnchor = offsets.NormalizedAnchor;
                }

                internal void InvalidateTeleport()
                {
                    TeleportUiCreated = false;
                    TeleportMoving = false;
                    TeleportShowMoveButton = false;
                    TeleportMode = null;
                    TeleportTimeText = null;
                    TeleportOffsetMin = default;
                    TeleportOffsetMax = default;
                    TeleportNormalizedAnchor = default;
                }

                internal void ResetRefresh(UiType type)
                {
                    switch (type)
                    {
                        case UiType.Cooldown: Cooldown = 0d; break;
                        case UiType.Delay: Delay = 0d; InvalidateDelay(); break;
                        case UiType.Lockout: Lockout = 0d; break;
                        case UiType.Status: ResetStatus(); break;
                        case UiType.Teleport: TeleportRefresh = 0d; break;
                    }
                }
            }

            public UiPalette GetPalette()
            {
                UIThemeSettings theme = config.UI.Theme ??= new();
                return new(
                    ConvertHexToRGBA(theme.BackgroundColor, theme.BackgroundAlpha),
                    ConvertHexToRGBA(theme.PanelColor, 1f),
                    ConvertHexToRGBA(theme.CellColor, 1f),
                    ConvertHexToRGBA(theme.DividerColor, 1f),
                    ConvertHexToRGBA(theme.ProgressBackgroundColor, 1f),
                    ConvertHexToRGBA(theme.AccentColor, 1f),
                    ConvertHexToRGBA(theme.TextColor, 1f),
                    ConvertHexToRGBA(theme.MutedTextColor, 1f));
            }

            public static bool IsMovableUi(UiType type)
            {
                return type is UiType.Buyable or UiType.Cooldown or UiType.Delay or UiType.Lockout or UiType.PasteProgress or UiType.Status or UiType.Teleport;
            }

            private float GetMoveModeSeconds(UiType type)
            {
                return type switch
                {
                    UiType.Buyable => config.UI.Buyable.MoveModeSeconds,
                    UiType.Cooldown => config.UI.BuyableCooldowns.MoveModeSeconds,
                    UiType.Delay => config.UI.Delay.MoveModeSeconds,
                    UiType.Lockout => config.UI.Lockout.MoveModeSeconds,
                    UiType.PasteProgress => config.UI.PasteProgress.MoveModeSeconds,
                    UiType.Status => config.UI.Status.MoveModeSeconds,
                    UiType.Teleport => config.UI.Teleport.MoveModeSeconds,
                    _ => 10f
                };
            }

            public static void AddCuiPanel(CuiElementContainer container, string color, string amin, string amax, string omin, string omax, string parent, string name, bool cursor = false, bool draggable = false, bool keyboard = false, bool persistentCursor = false, bool? draggableEnabled = null)
            {
                CuiPanel panel = new()
                {
                    CursorEnabled = cursor,
                    KeyboardEnabled = keyboard,
                    Image = { Color = color },
                    RectTransform = { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
                };

                if (!draggable)
                {
                    container.Add(panel, parent, name, name);
                    return;
                }

                CuiElement host = new()
                {
                    Name = name,
                    Parent = parent,
                    DestroyUi = name
                };

                if (panel.Image != null)
                {
                    host.Components.Add(panel.Image);
                }

                if (panel.RawImage != null)
                {
                    host.Components.Add(panel.RawImage);
                }

                if (panel.RectTransform != null)
                {
                    host.Components.Add(panel.RectTransform);
                }

                if (panel.CursorEnabled || persistentCursor)
                {
                    host.Components.Add(new CuiNeedsCursorComponent { Enabled = panel.CursorEnabled });
                }

                if (panel.KeyboardEnabled)
                {
                    host.Components.Add(new CuiNeedsKeyboardComponent());
                }

                host.Components.Add(CreateCuiDraggableComponent(draggableEnabled));

                container.Add(host);
            }

            private static CuiDraggableComponent CreateCuiDraggableComponent(bool? enabled = null)
            {
                return new()
                {
                    LimitToParent = true,
                    MaxDistance = -1f,
                    AllowSwapping = false,
                    DropAnywhere = true,
                    DragAlpha = 0.98f,
                    ParentLimitIndex = 1,
                    ParentPadding = "0 0",
                    AnchorOffset = "0 0",
                    KeepOnTop = false,
                    PositionRPC = CommunityEntity.DraggablePositionSendType.NormalizedParent,
                    Enabled = enabled ?? true
                };
            }

            private static void AddCuiInteractionUpdate(CuiElementContainer container, string parent, string name, bool cursorEnabled, bool draggableEnabled)
            {
                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    Update = true,
                    Components =
                    {
                        new CuiNeedsCursorComponent { Enabled = cursorEnabled },
                        CreateCuiDraggableComponent(draggableEnabled)
                    }
                });
            }

            public static void AddCuiInput(CuiElementContainer container, string command, int characterLimit, string textColor, int fontSize, TextAnchor align, string amin, string amax, string omin, string omax, string parent, string name, bool bold = false)
            {
                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    Components =
                    {
                        new CuiInputFieldComponent { Text = string.Empty, Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf", FontSize = fontSize, Align = align, Color = textColor, CharsLimit = characterLimit, Command = command, HudMenuInput = true },
                        new CuiRectTransformComponent { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
                    }
                });
            }

            public static void AddCuiButton(CuiElementContainer container, string buttonColor, string command, string text, string textColor, int fontSize, TextAnchor align, string amin, string amax, string omin, string omax, string parent, string name, bool bold = false, string textName = null)
            {
                if (string.IsNullOrEmpty(textName))
                {
                    container.Add(new CuiButton
                    {
                        Button = { Color = buttonColor, Command = command },
                        Text = { Text = text, Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf", FontSize = fontSize, Align = align, Color = textColor },
                        RectTransform = { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
                    }, parent, name, name);
                    return;
                }

                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    DestroyUi = name,
                    Components =
                    {
                        new CuiButtonComponent { Color = buttonColor, Command = command },
                        new CuiRectTransformComponent { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = textName,
                    Parent = name,
                    DestroyUi = textName,
                    Components =
                    {
                        new CuiTextComponent { Text = text, Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf", FontSize = fontSize, Align = align, Color = textColor },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                    }
                });
            }

            public static void AddCuiElement(CuiElementContainer container, string text, int fontSize, TextAnchor align, string textColor, string amin, string amax, string omin, string omax, string parent, string name, bool bold = true, string distance = "1 -1")
            {
                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    Components =
                    {
                        new CuiTextComponent { Text = text, Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf", FontSize = fontSize, Align = align, Color = textColor },
                        new CuiOutlineComponent { Color = "0 0 0 0", Distance = distance },
                        new CuiRectTransformComponent { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
                    }
                });
            }

            public static void AddCuiTextUpdate(CuiElementContainer container, string text, string parent, string name, string color = null)
            {
                CuiTextComponent component = new() { Text = text ?? string.Empty, FontSize = 14 };

                if (color != null)
                {
                    component.Color = color;
                }

                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    Update = true,
                    Components = { component }
                });
            }

            private static void AddCuiImageUpdate(CuiElementContainer container, string color, string parent, string name)
            {
                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    Update = true,
                    Components = { new CuiImageComponent { Color = color } }
                });
            }

            public static void AddCuiRectTransformUpdate(CuiElementContainer container, string anchorMax, string parent, string name)
            {
                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    Update = true,
                    Components = { new CuiRectTransformComponent { AnchorMax = anchorMax } }
                });
            }

            public static double ParseHexComponent(string hex, int start, int length)
            {
                if (string.IsNullOrWhiteSpace(hex))
                {
                    return 255d;
                }

                string value = hex.Trim().TrimStart('#');
                return value.Length >= start + length && int.TryParse(value.AsSpan(start, length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int number) ? number : 255d;
            }

            public static string GetContrastColor(string hex)
            {
                double luminance = (ParseHexComponent(hex, 0, 2) * 299d + ParseHexComponent(hex, 2, 2) * 587d + ParseHexComponent(hex, 4, 2) * 114d) / 1000d;
                return luminance >= 128d ? "0 0 0 1" : "1 1 1 1";
            }

            public static string ConvertHexToRGBA(string hex, float alpha)
            {
                double r = ParseHexComponent(hex, 0, 2) / 255d;
                double g = ParseHexComponent(hex, 2, 2) / 255d;
                double b = ParseHexComponent(hex, 4, 2) / 255d;
                return FormattableString.Invariant($"{r:0.####} {g:0.####} {b:0.####} {Mathf.Clamp01(alpha):0.####}");
            }

            private static string Vec2ToString(Vector2 value)
            {
                return FormattableString.Invariant($"{value.x:0.######} {value.y:0.######}");
            }

            private static void AddMovablePanel(CuiElementContainer container, string parent, string name, string color, string defaultAnchor, UiOffsets offsets, float width, float height, bool cursorEnabled, bool moving, bool allowDragging)
            {
                bool draggableEnabled = moving || allowDragging;

                if (offsets.NormalizedAnchor != Vector2.zero)
                {
                    string anchor = Vec2ToString(offsets.NormalizedAnchor);
                    AddCuiPanel(container, color, anchor, anchor,
                        FormattableString.Invariant($"{-width * 0.5f:0.###} {-height * 0.5f:0.###}"),
                        FormattableString.Invariant($"{width * 0.5f:0.###} {height * 0.5f:0.###}"),
                        parent, name, cursorEnabled, true, persistentCursor: true, draggableEnabled: draggableEnabled);
                    return;
                }

                Vector2 center = (offsets.Min + offsets.Max) * 0.5f;
                Vector2 half = new(width * 0.5f, height * 0.5f);
                Vector2 min = center - half;
                Vector2 max = center + half;
                AddCuiPanel(container, color, defaultAnchor, defaultAnchor,
                    FormattableString.Invariant($"{min.x:0.###} {min.y:0.###}"),
                    FormattableString.Invariant($"{max.x:0.###} {max.y:0.###}"),
                    parent, name, cursorEnabled, true, persistentCursor: true, draggableEnabled: draggableEnabled);
            }

            public bool IsMeaningfulDrag(UiOffsets offsets, Vector2 normalizedPosition)
            {
                return offsets == null || offsets.NormalizedAnchor == Vector2.zero ||
                    Mathf.Abs(offsets.NormalizedAnchor.x - normalizedPosition.x) > UI_DRAG_EPSILON ||
                    Mathf.Abs(offsets.NormalizedAnchor.y - normalizedPosition.y) > UI_DRAG_EPSILON;
            }

            public void StartBuyablePanic(BasePlayer player)
            {
                if (player == null)
                {
                    return;
                }

                UIBuyableTypeSelectorSettings selector = config.UI.Buyable.TypeSelector ??= new();
                UIBuyablePanicCloseSettings settings = selector.Panic ??= new();

                if (!selector.Enabled || !settings.Enabled || settings.JumpPresses <= 0 && settings.MovementPresses <= 0)
                {
                    StopBuyablePanic(player);
                    return;
                }

                BuyableUiPanic panic = player.GetComponent<BuyableUiPanic>();
                if (panic == null)
                {
                    panic = player.gameObject.AddComponent<BuyableUiPanic>();
                }

                panic.Setup(Instance, player, settings);
            }

            public void StopBuyablePanic(BasePlayer player)
            {
                if (player != null && player.GetComponent<BuyableUiPanic>() is BuyableUiPanic panic)
                {
                    UnityEngine.Object.Destroy(panic);
                }
            }

            public void DestroyBuyableUi(BasePlayer player)
            {
                if (player == null)
                {
                    return;
                }

                StopBuyablePanic(player);
                DestroyTimer(player, player.userID, UiType.Buyable);
                CuiHelper.DestroyUi(player, BUYABLE_UI);
            }

            private static void DestroyUiElements(BasePlayer player)
            {
                if (player == null)
                {
                    return;
                }

                if (player.GetComponent<BuyableUiPanic>() is BuyableUiPanic panic)
                {
                    UnityEngine.Object.Destroy(panic);
                }

                CuiHelper.DestroyUi(player, BUYABLE_UI);
                CuiHelper.DestroyUi(player, COOLDOWN_UI);
                CuiHelper.DestroyUi(player, DELAY_UI);
                CuiHelper.DestroyUi(player, LOCKOUT_UI);
                CuiHelper.DestroyUi(player, PASTE_PROGRESS_UI);
                CuiHelper.DestroyUi(player, STATUS_UI);
                CuiHelper.DestroyUi(player, TARGET_INFO_UI);
                CuiHelper.DestroyUi(player, TELEPORT_UI);
                CuiHelper.DestroyUi(player, CONFIG_IMPORT_UI);
            }

            private void DestroyMoverTimers(ulong userid)
            {
                if (!Movers.Remove(userid, out Dictionary<UiType, Timer> types))
                {
                    return;
                }

                foreach (Timer timer in types.Values)
                {
                    if (timer is { Destroyed: false })
                    {
                        timer.Destroy();
                    }
                }
            }

            public void RemoveUser(ulong userid)
            {
                if (users.Remove(userid, out PlayerUi ui))
                {
                    ui.Cooldown = 0d;
                    ui.Delay = 0d;
                    ui.Lockout = 0d;
                    ui.TeleportRefresh = 0d;
                    ui.ResetStatus();
                    ui.InvalidatePaste();
                    ui.InvalidateDelay();
                    ui.InvalidateTeleport();
                }

                DestroyMoverTimers(userid);
                PrivateEvents.Remove(userid);
                PublicEvents.Remove(userid);

                if (Teleport.Remove(userid, out TimeSettings teleport))
                {
                    teleport.Destroy();
                }
            }

            private void TryRemoveUser(ulong userid, PlayerUi ui)
            {
                if (ui != null && !ui.HasRefresh && !ui.StatusUiCreated && !ui.PasteUiCreated && !ui.DelayUiCreated && !ui.TeleportUiCreated && !Movers.ContainsKey(userid))
                {
                    users.Remove(userid);
                }
            }

            public void DestroyAllUi(BasePlayer player)
            {
                if (player == null)
                {
                    return;
                }

                DestroyUiElements(player);
                Instance._configPresetController?.ForgetImportUi(player.userID);
                RemoveUser(player.userID);
            }

            public bool DestroyUi(BasePlayer player, UiType type)
            {
                if (player == null)
                {
                    return false;
                }

                ulong userid = player.userID;
                bool hadState = users.TryGetValue(userid, out PlayerUi ui);
                TrySetMoveUi(player, type, true);

                switch (type)
                {
                    case UiType.Buyable:
                        StopBuyablePanic(player);
                        CuiHelper.DestroyUi(player, BUYABLE_UI);
                        PrivateEvents.Remove(userid);
                        break;
                    case UiType.Cooldown:
                        CuiHelper.DestroyUi(player, COOLDOWN_UI);
                        if (ui != null) ui.Cooldown = 0d;
                        break;
                    case UiType.Delay:
                        CuiHelper.DestroyUi(player, DELAY_UI);
                        if (ui != null)
                        {
                            ui.Delay = 0d;
                            ui.InvalidateDelay();
                        }
                        break;
                    case UiType.Lockout:
                        CuiHelper.DestroyUi(player, LOCKOUT_UI);
                        if (ui != null) ui.Lockout = 0d;
                        break;
                    case UiType.PasteProgress:
                        CuiHelper.DestroyUi(player, PASTE_PROGRESS_UI);
                        ui?.InvalidatePaste();
                        break;
                    case UiType.Status:
                        CuiHelper.DestroyUi(player, STATUS_UI);
                        ui?.ResetStatus();
                        break;
                    case UiType.Teleport:
                        CuiHelper.DestroyUi(player, TELEPORT_UI);
                        if (ui != null)
                        {
                            ui.TeleportRefresh = 0d;
                            ui.InvalidateTeleport();
                        }
                        if (Teleport.Remove(userid, out TimeSettings teleport))
                        {
                            teleport.Destroy();
                        }
                        break;
                }

                if (ui != null)
                {
                    TryRemoveUser(userid, ui);
                }

                return hadState;
            }

            public void ResetStatusUi2(ulong userid)
            {
                DestroyTimer(null, userid, UiType.Status);

                if (users.TryGetValue(userid, out PlayerUi ui))
                {
                    ui.ResetStatus();
                    TryRemoveUser(userid, ui);
                }
            }

            public void UpdateUi(BasePlayer player, UiType type)
            {
                if (config == null || player == null || !player.IsConnected)
                {
                    return;
                }

                bool moving = IsMovingUi(player, type);

                switch (type)
                {
                    case UiType.Buyable:
                        if (config.UI.Buyable.Enabled)
                        {
                            ShowBuyableUi(player, moving);
                        }
                        else
                        {
                            DestroyBuyableUi(player);
                        }
                        return;
                    case UiType.Cooldown:
                        {
                            PlayerUi state = TryAddUser(player);
                            state.Cooldown = 0d;
                            if (config.UI.BuyableCooldowns.Enabled && ShowBuyableCooldownsUi(player, moving))
                            {
                                state.Cooldown = Time.realtimeSinceStartupAsDouble + 60d;
                                EnsureStatusRefreshCoroutine();
                            }
                            else
                            {
                                PrivateEvents.Remove(player.userID);
                                TryRemoveUser(player.userID, state);
                            }
                            return;
                        }
                    case UiType.Delay:
                        {
                            PlayerUi state = TryAddUser(player);
                            state.Delay = 0d;
                            if (config.UI.Delay.Enabled && ShowDelayUi(player, moving))
                            {
                                state.Delay = Time.realtimeSinceStartupAsDouble + 1d;
                                EnsureStatusRefreshCoroutine();
                            }
                            else
                            {
                                TryRemoveUser(player.userID, state);
                            }
                            return;
                        }
                    case UiType.Lockout:
                        {
                            PlayerUi state = TryAddUser(player);
                            state.Lockout = 0d;
                            if (config.UI.Lockout.Enabled && ShowLockoutsUi(player, moving))
                            {
                                state.Lockout = Time.realtimeSinceStartupAsDouble + 60d;
                                EnsureStatusRefreshCoroutine();
                            }
                            else
                            {
                                PublicEvents.Remove(player.userID);
                                TryRemoveUser(player.userID, state);
                            }
                            return;
                        }
                    case UiType.PasteProgress:
                        Instance._pasteEngine?.RefreshProgressUi(player);
                        return;
                    case UiType.Status:
                        if (!config.UI.Status.Enabled || !Instance.Get(player.transform.position, out RaidableBase raid))
                        {
                            DestroyUi(player, UiType.Status);
                            return;
                        }

                        RefreshStatusUi(player, raid, moving, true);
                        return;
                    case UiType.Teleport:
                        {
                            PlayerUi state = TryAddUser(player);
                            state.TeleportRefresh = 0d;
                            if (ShowBuyableTeleportUi(player, moving))
                            {
                                state.TeleportRefresh = Time.realtimeSinceStartupAsDouble + 1d;
                                EnsureStatusRefreshCoroutine();
                            }
                            else
                            {
                                TryRemoveUser(player.userID, state);
                            }
                            return;
                        }
                }
            }

            public void DestroyAll()
            {
                if (statusRefreshCoroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(statusRefreshCoroutine);
                    statusRefreshCoroutine = null;
                }

                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    DestroyAllUi(player);
                }

                using var moverIds = Movers.Keys.ToPooledList();

                for (int i = 0; i < moverIds.Count; i++)
                {
                    DestroyMoverTimers(moverIds[i]);
                }

                users.Clear();
                PrivateEvents.Clear();
                PublicEvents.Clear();

                foreach (TimeSettings teleport in Teleport.Values)
                {
                    teleport?.Destroy();
                }

                Teleport.Clear();
            }

            private string GetPurchasePrice(string mode, BasePlayer player, bool free, out double price)
            {
                price = 0d;

                if (!Instance.CanSpawnDifficultyToday(RaidableType.Purchased, mode) || config.Settings.Buyable.Limits.Get(mode) < 0)
                {
                    return null;
                }

                string userid = player.UserIDString;
                string text = rf(mx($"Mode{mode}", userid));

                if (config.Settings.Management.TitleCase == true)
                {
                    text = text.TitleCase();
                }

                if (free)
                {
                    return mx("PriceText", userid, text, mx("BuyableFree", userid));
                }

                using var prices = DisposableList<string>();

                if (config.Settings.Include.Custom && config.Settings.Custom.TryGetValue(mode, out List<CustomCostOptions> customCosts) && !customCosts.IsNullOrEmpty())
                {
                    for (int i = 0; i < customCosts.Count; i++)
                    {
                        CustomCostOptions customCost = customCosts[i];

                        if (customCost.isItem)
                        {
                            prices.Add(mx("CustomDepositFormat", userid, customCost.Amount, string.IsNullOrWhiteSpace(customCost.Name) ? customCost.Shortname : customCost.Name));
                        }

                        if (customCost.isPlugin)
                        {
                            prices.Add(mx("CustomDepositFormat", userid, customCost.Plugin.Amount, customCost.GetCurrencyName()));
                        }
                    }
                }

                if (config.Settings.Include.ServerRewards)
                {
                    price = config.Settings.ServerRewards.Get(mode);
                    if (price > 0d)
                    {
                        prices.Add(mx("RP", userid, (int)price));
                    }
                }

                if (config.Settings.Include.Economics)
                {
                    price = config.Settings.Economics.Get(mode);
                    if (price > 0d)
                    {
                        prices.Add(mx("$", userid, price));
                    }
                }

                return prices.Count == 0 ? null : mx("PriceText", userid, text, string.Join(", ", prices));
            }

            public float EstimateTextWidth(string text, float fontSize, float widthAdjustmentPercentage = 5f)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return 0f;
                }

                float baseWidth = text.Length * fontSize * 0.475f;
                return baseWidth * (1f + widthAdjustmentPercentage / 100f);
            }

            public float GetAdjustedTextWidth(string text, float fontSize, float maxWidth, float widthAdjustmentPercentage = 5f)
            {
                float width = EstimateTextWidth(text, fontSize, widthAdjustmentPercentage);
                return Mathf.Min(width, maxWidth);
            }

            private void AddPurchaseTypeButton(CuiElementContainer container, UIBuyableSettings ui, UIBuyableTypeSelectorSettings selector, string parent, string name, string text, string command, bool available, string enabledColor, float xMin, float xMax, float yMin, float yMax)
            {
                string buttonColor = ConvertHexToRGBA(available ? enabledColor : selector.DisabledButtonColor, ui.ButtonAlpha);
                string textColor = ConvertHexToRGBA(available ? selector.ButtonTextColor : selector.DisabledTextColor, 1f);
                AddCuiButton(container, buttonColor, available ? command : string.Empty, text, textColor, Mathf.Clamp(ui.FontSize - 1, 9, 20), TextAnchor.MiddleCenter, "1 1", "1 1", FormattableString.Invariant($"{xMin:0.###} {yMin:0.###}"), FormattableString.Invariant($"{xMax:0.###} {yMax:0.###}"), parent, name);
            }

            private static int CompareBuyableButtons((string mode, string text, double value, int level) left, (string mode, string text, double value, int level) right)
            {
                int result = left.value.CompareTo(right.value);
                return result != 0 ? result : left.level.CompareTo(right.level);
            }

            private static void SortBuyableButtons(List<(string mode, string text, double value, int level)> buttons)
            {
                for (int i = 1; i < buttons.Count; i++)
                {
                    (string mode, string text, double value, int level) current = buttons[i];
                    int index = i - 1;

                    while (index >= 0 && CompareBuyableButtons(buttons[index], current) > 0)
                    {
                        buttons[index + 1] = buttons[index];
                        index--;
                    }

                    buttons[index + 1] = current;
                }
            }

            public void ShowBuyableUi(BasePlayer player, bool moveUI)
            {
                UIBuyableSettings ui = config.UI.Buyable;

                if (player == null || !player.IsConnected || !ui.Enabled)
                {
                    DestroyBuyableUi(player);
                    return;
                }

                StopBuyablePanic(player);
                CuiHelper.DestroyUi(player, BUYABLE_UI);

                UIBuyableTypeSelectorSettings selector = ui.TypeSelector ??= new();
                selector.Panic ??= new();
                int fontSize = Mathf.Clamp(ui.FontSize, 9, 20);
                bool free = Instance.IsFreePurchase(player.GetIPlayer(), player, Array.Empty<string>());
                float maxTextWidth = 0f;
                List<string> modes = Instance.GetRaidableModes();
                using var buttons = DisposableList<(string mode, string text, double value, int level)>();

                for (int i = 0; i < modes.Count; i++)
                {
                    string mode = modes[i];
                    string text = GetPurchasePrice(mode, player, free, out double price);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    maxTextWidth = Mathf.Max(maxTextWidth, GetAdjustedTextWidth(text, fontSize, 520f));
                    int level = Instance.GetLevelFromMode(mode);
                    double value = ui.Price && !free || level == -1 ? price : level;
                    buttons.Add((mode, text, value, level));
                }

                if (buttons.Count == 0)
                {
                    DestroyBuyableUi(player);
                    SendError(player, RaidableType.Purchased);
                    return;
                }

                SortBuyableButtons(buttons);

                UiPalette palette = GetPalette();
                bool useTheme = ui.UseThemeColors;
                float panelAlpha = ui.PanelAlpha ?? 0.98f;
                string background = useTheme ? palette.Background : ConvertHexToRGBA(ui.PanelColor, panelAlpha);
                string panel = useTheme ? palette.Panel : ConvertHexToRGBA(ui.TitlePanelColor, panelAlpha);
                string cell = useTheme ? palette.Cell : ConvertHexToRGBA(ui.TitlePanelColor, Mathf.Clamp01(panelAlpha * 0.9f));
                string accent = useTheme ? palette.Accent : ConvertHexToRGBA(ui.CloseColor, 1f);
                string textColor = useTheme ? palette.Text : "1 1 1 1";
                string muted = useTheme ? palette.Muted : "0.72 0.72 0.72 1";
                string closeColor = ConvertHexToRGBA(ui.XTextColor, 1f);
                float selectorButtonWidth = Mathf.Max(35f, selector.ButtonWidth);
                float selectorSpacing = Mathf.Max(0f, selector.ButtonSpacing);
                float selectorAreaWidth = selectorButtonWidth * 2f + selectorSpacing;
                float panelWidth = selector.Enabled
                    ? Mathf.Clamp(maxTextWidth + selectorAreaWidth + 58f, 320f, 680f)
                    : Mathf.Clamp(maxTextWidth + 44f, 250f, 560f);
                float rowHeight = Mathf.Max(30f, fontSize + 15f);
                float rowSpacing = 4f;
                float headerHeight = 39f;
                float bodyHeight = 12f + buttons.Count * rowHeight + Math.Max(0, buttons.Count - 1) * rowSpacing;
                float panelHeight = headerHeight + bodyHeight + 8f;
                bool pveAvailable = !selector.Enabled || Instance.CanBuyRaidType(player, 1);
                bool pvpAvailable = !selector.Enabled || Instance.CanBuyRaidType(player, 2);
                UiOffsets offsets = GetOffsets(player.userID, UiType.Buyable);
                CuiElementContainer container = new();

                AddMovablePanel(container, BUYABLE_PARENT, BUYABLE_UI, background, "0.5 0.1", offsets, panelWidth, panelHeight, true, moveUI, false);
                AddCuiPanel(container, accent, "0 1", "1 1", "0 -2", "0 0", BUYABLE_UI, $"{BUYABLE_UI}_Accent");
                AddCuiPanel(container, accent, "0 1", "0 1", "10 -24", "16 -18", BUYABLE_UI, $"{BUYABLE_UI}_Dot");
                AddCuiElement(container, mx("Buy Raids", player.UserIDString), Mathf.Min(22, fontSize + 3), TextAnchor.MiddleLeft, accent,
                    "0 1", "1 1", "24 -34", ui.ShowMoveButton ? "-100 -4" : "-48 -4", BUYABLE_UI, $"{BUYABLE_UI}_Title");

                float closeMin = -36f;
                AddCuiButton(container, panel, "ui_buyraid closeui", "×", closeColor, Mathf.Max(16, fontSize + 2), TextAnchor.MiddleCenter,
                    "1 1", "1 1", "-34 -33", "-8 -7", BUYABLE_UI, $"{BUYABLE_UI}_Close");

                if (ui.ShowMoveButton)
                {
                    AddCuiButton(container, panel, $"rb_ui_move {UiType.Buyable}", mx(moveUI ? "UIStatusDone" : "UIStatusMove", player.UserIDString), moveUI ? accent : muted,
                        Mathf.Max(8, fontSize - 2), TextAnchor.MiddleCenter, "1 1", "1 1", "-92 -33", FormattableString.Invariant($"{closeMin - 4f:0.###} -7"), BUYABLE_UI, $"{BUYABLE_UI}_Move");
                }

                AddCuiPanel(container, cell, "0 0", "1 1", "8 8", "-8 -39", BUYABLE_UI, $"{BUYABLE_UI}_Body");
                string body = $"{BUYABLE_UI}_Body";

                for (int i = 0; i < buttons.Count; i++)
                {
                    var button = buttons[i];
                    string mode = button.mode;
                    string priceText = button.text;
                    float rowTop = -6f - i * (rowHeight + rowSpacing);
                    float rowBottom = rowTop - rowHeight;
                    string safeMode = mode.Replace(" ", "_");
                    string token = mode.Replace(" ", "__");
                    string buttonColorHex = ui.Difficulty ? config.Settings.Management.Colors2.Get(mode) : ui.GetButton(mode);
                    string buttonColor = ConvertHexToRGBA(buttonColorHex, ui.ButtonAlpha);
                    string buttonTextColor = ui.Contrast ? GetContrastColor(buttonColorHex) : ConvertHexToRGBA(ui.GetText(mode), 1f);

                    if (!selector.Enabled)
                    {
                        AddCuiButton(container, buttonColor, $"ui_buyraid {token}", priceText, buttonTextColor, fontSize, TextAnchor.MiddleCenter,
                            "0 1", "1 1", FormattableString.Invariant($"8 {rowBottom:0.###}"), FormattableString.Invariant($"-8 {rowTop:0.###}"), body, $"{BUYABLE_UI}_{safeMode}_Buy");
                        continue;
                    }

                    int visibleButtons = selector.ShowUnavailableButtons ? 2 : (pveAvailable ? 1 : 0) + (pvpAvailable ? 1 : 0);
                    float visibleWidth = visibleButtons == 0 ? 0f : visibleButtons == 1 ? selectorButtonWidth : selectorAreaWidth;
                    float difficultyRight = visibleButtons == 0 ? -8f : -visibleWidth - selectorSpacing - 8f;
                    string modePanel = $"{BUYABLE_UI}_{safeMode}_Mode";

                    AddCuiPanel(container, buttonColor, "0 1", "1 1", FormattableString.Invariant($"8 {rowBottom:0.###}"), FormattableString.Invariant($"{difficultyRight:0.###} {rowTop:0.###}"), body, modePanel);
                    AddCuiElement(container, priceText, fontSize, TextAnchor.MiddleCenter, buttonTextColor, "0 0", "1 1", "6 0", "-6 0", modePanel, $"{modePanel}_Text", false);

                    string pveCommand = selector.RequireConfirmation ? $"ui_buyraid confirm_type {token} pve" : $"ui_buyraid {token} pve";
                    string pvpCommand = selector.RequireConfirmation ? $"ui_buyraid confirm_type {token} pvp" : $"ui_buyraid {token} pvp";
                    string pveText = mx("BuyablePVE", player.UserIDString);
                    string pvpText = mx("BuyablePVP", player.UserIDString);

                    if (visibleButtons == 1)
                    {
                        if (pveAvailable)
                        {
                            AddPurchaseTypeButton(container, ui, selector, body, $"{BUYABLE_UI}_{safeMode}_PVE", pveText, pveCommand, true, selector.PVEButtonColor,
                                -selectorButtonWidth - 8f, -8f, rowBottom, rowTop);
                        }
                        else if (pvpAvailable)
                        {
                            AddPurchaseTypeButton(container, ui, selector, body, $"{BUYABLE_UI}_{safeMode}_PVP", pvpText, pvpCommand, true, selector.PVPButtonColor,
                                -selectorButtonWidth - 8f, -8f, rowBottom, rowTop);
                        }
                    }
                    else if (visibleButtons > 1)
                    {
                        float pveMin = -selectorAreaWidth - 8f;
                        float pveMax = -selectorButtonWidth - selectorSpacing - 8f;
                        float pvpMin = -selectorButtonWidth - 8f;
                        float pvpMax = -8f;

                        if (selector.ShowUnavailableButtons || pveAvailable)
                        {
                            AddPurchaseTypeButton(container, ui, selector, body, $"{BUYABLE_UI}_{safeMode}_PVE", pveText, pveCommand, pveAvailable, selector.PVEButtonColor,
                                pveMin, pveMax, rowBottom, rowTop);
                        }

                        if (selector.ShowUnavailableButtons || pvpAvailable)
                        {
                            AddPurchaseTypeButton(container, ui, selector, body, $"{BUYABLE_UI}_{safeMode}_PVP", pvpText, pvpCommand, pvpAvailable, selector.PVPButtonColor,
                                pvpMin, pvpMax, rowBottom, rowTop);
                        }
                    }
                }

                bool added = CuiHelper.AddUi(player, container);

                if (config.UI.BuyableCooldowns.BuyOnly)
                {
                    PrivateEvents.Add(player.userID);
                    UpdateUi(player, UiType.Cooldown);
                }

                if (config.UI.Lockout.BuyOnly)
                {
                    PublicEvents.Add(player.userID);
                    UpdateUi(player, UiType.Lockout);
                }

                if (moveUI)
                {
                    StopBuyablePanic(player);
                    if (!IsMovingUi(player, UiType.Buyable))
                    {
                        TrySetMoveUi(player, UiType.Buyable);
                    }
                }
                else if (added && selector.Enabled)
                {
                    StartBuyablePanic(player);
                }
                else
                {
                    StopBuyablePanic(player);
                }
            }

            public void ShowBuyableConfirmationUi(BasePlayer player, string mode, string type)
            {
                if (player == null || !player.IsConnected)
                {
                    return;
                }

                UIBuyableSettings ui = config.UI.Buyable;
                UIBuyableTypeSelectorSettings selector = ui.TypeSelector ??= new();
                selector.Panic ??= new();
                int purchaseType = type == "pve" ? 1 : type == "pvp" ? 2 : -1;

                if (!ui.Enabled || !selector.Enabled || !Instance.CanBuyRaidType(player, purchaseType))
                {
                    Notify(player, "BuyableTypeUnavailable");
                    return;
                }

                bool free = Instance.IsFreePurchase(player.GetIPlayer(), player, Array.Empty<string>());
                string priceText = GetPurchasePrice(mode, player, free, out _);

                if (string.IsNullOrWhiteSpace(priceText))
                {
                    SendError(player, RaidableType.Purchased);
                    return;
                }

                StopBuyablePanic(player);
                CuiHelper.DestroyUi(player, BUYABLE_UI);

                int fontSize = Mathf.Clamp(ui.FontSize, 9, 20);
                string userid = player.UserIDString;
                string typeText = mx(purchaseType == 2 ? "BuyablePVP" : "BuyablePVE", userid);
                string titleText = mx("BuyableConfirmTitle", userid);
                string questionText = mx("BuyableConfirmQuestion", userid, typeText, priceText);
                string token = mode.Replace(" ", "__");
                float panelWidth = Mathf.Clamp(Mathf.Max(320f, GetAdjustedTextWidth(questionText, fontSize, 600f) + 46f), 320f, 640f);
                float panelHeight = 148f;
                float panelAlpha = ui.PanelAlpha ?? 0.98f;
                UiPalette palette = GetPalette();
                bool useTheme = ui.UseThemeColors;
                string background = useTheme ? palette.Background : ConvertHexToRGBA(ui.PanelColor, panelAlpha);
                string panel = useTheme ? palette.Panel : ConvertHexToRGBA(ui.TitlePanelColor, panelAlpha);
                string cell = useTheme ? palette.Cell : panel;
                string accentHex = purchaseType == 2 ? selector.PVPButtonColor : selector.PVEButtonColor;
                string accent = ConvertHexToRGBA(accentHex, 1f);
                string textColor = useTheme ? palette.Text : ConvertHexToRGBA(selector.ButtonTextColor, 1f);
                string muted = useTheme ? palette.Muted : "0.72 0.72 0.72 1";
                string closeColor = ConvertHexToRGBA(ui.XTextColor, 1f);
                bool moveUI = IsMovingUi(player, UiType.Buyable);
                UiOffsets offsets = GetOffsets(player.userID, UiType.Buyable);
                CuiElementContainer container = new();

                AddMovablePanel(container, BUYABLE_PARENT, BUYABLE_UI, background, "0.5 0.1", offsets, panelWidth, panelHeight, true, moveUI, false);
                AddCuiPanel(container, accent, "0 1", "1 1", "0 -2", "0 0", BUYABLE_UI, $"{BUYABLE_UI}_Accent");
                AddCuiPanel(container, accent, "0 1", "0 1", "10 -24", "16 -18", BUYABLE_UI, $"{BUYABLE_UI}_Dot");
                AddCuiElement(container, titleText, Mathf.Min(22, fontSize + 3), TextAnchor.MiddleLeft, accent,
                    "0 1", "1 1", "24 -34", ui.ShowMoveButton ? "-100 -4" : "-48 -4", BUYABLE_UI, $"{BUYABLE_UI}_Title");
                AddCuiButton(container, panel, "ui_buyraid closeui", "×", closeColor, Mathf.Max(16, fontSize + 2), TextAnchor.MiddleCenter,
                    "1 1", "1 1", "-34 -33", "-8 -7", BUYABLE_UI, $"{BUYABLE_UI}_Close");

                if (ui.ShowMoveButton)
                {
                    AddCuiButton(container, panel, $"rb_ui_move {UiType.Buyable}", mx(moveUI ? "UIStatusDone" : "UIStatusMove", userid), moveUI ? accent : muted,
                        Mathf.Max(8, fontSize - 2), TextAnchor.MiddleCenter, "1 1", "1 1", "-92 -33", "-40 -7", BUYABLE_UI, $"{BUYABLE_UI}_Move");
                }

                AddCuiPanel(container, cell, "0 0", "1 1", "8 8", "-8 -39", BUYABLE_UI, $"{BUYABLE_UI}_Body");
                string body = $"{BUYABLE_UI}_Body";
                AddCuiPanel(container, accent, "0 1", "0 1", "12 -24", "18 -18", body, $"{BUYABLE_UI}_TypeDot");
                AddCuiElement(container, typeText, fontSize, TextAnchor.MiddleLeft, accent, "0 1", "0.3 1", "26 -34", "0 -8", body, $"{BUYABLE_UI}_Type");
                AddCuiElement(container, questionText, fontSize, TextAnchor.MiddleLeft, textColor, "0 1", "1 1", "12 -64", "-12 -34", body, $"{BUYABLE_UI}_Question", false);
                AddCuiButton(container, ConvertHexToRGBA(accentHex, ui.ButtonAlpha), $"ui_buyraid confirm_purchase {token} {type}", mx("BuyableConfirm", userid), ConvertHexToRGBA(selector.ButtonTextColor, 1f),
                    fontSize, TextAnchor.MiddleCenter, "0 0", "0.5 0", "12 10", "-3 40", body, $"{BUYABLE_UI}_Confirm");
                AddCuiButton(container, panel, "ui_buyraid back_to_buyable", mx("BuyableBack", userid), textColor,
                    fontSize, TextAnchor.MiddleCenter, "0.5 0", "1 0", "3 10", "-12 40", body, $"{BUYABLE_UI}_Back");

                if (CuiHelper.AddUi(player, container) && !moveUI)
                {
                    StartBuyablePanic(player);
                }
            }

            private void SendError(BasePlayer player, RaidableType type)
            {
                if (!config.Settings.Include.Any)
                {
                    Notify(player, "NoBuyableEventsCostsEnabled");
                }

                if (!config.Settings.ServerRewards.Any() && !config.Settings.Economics.Any() && !config.Settings.AnyCustomCost())
                {
                    Notify(player, "NoBuyableEventsCostsConfigured");
                }

                bool allPvp = Instance.Buildings.Profiles.Count > 0;
                foreach (KeyValuePair<string, BaseProfile> profile in Instance.Buildings.Profiles)
                {
                    if (profile.Value?.Options?.AllowPVP != true)
                    {
                        allPvp = false;
                        break;
                    }
                }

                if (!Instance.AllowBuyingPVP && allPvp)
                {
                    Notify(player, "NoBuyableEventsPVP");
                }

                if (!config.Settings.Management.Amounts.Any())
                {
                    Notify(player, "NoBuyableEventsEnabled");
                }

                bool canSpawnToday = false;
                List<string> modes = Instance.GetRaidableModes();
                for (int i = 0; i < modes.Count; i++)
                {
                    if (Instance.CanSpawnDifficultyToday(type, modes[i]))
                    {
                        canSpawnToday = true;
                        break;
                    }
                }

                if (!canSpawnToday)
                {
                    Notify(player, "NoBuyableEventsToday");
                }
            }

            private void ExpireTeleport(ulong userid, TimeSettings expected)
            {
                if (!Teleport.TryGetValue(userid, out TimeSettings current) || !ReferenceEquals(current, expected))
                {
                    return;
                }

                Teleport.Remove(userid);
                expected.Timer = null;
                BasePlayer player = null;
                PlayerUi state = null;

                if (users.TryGetValue(userid, out state))
                {
                    player = state.player;
                    state.TeleportRefresh = 0d;
                    state.InvalidateTeleport();
                }

                player ??= RustCore.FindPlayerById(userid);
                DestroyTimer(player, userid, UiType.Teleport, false);

                if (state != null)
                {
                    TryRemoveUser(userid, state);
                }

                if (player != null && player.IsConnected)
                {
                    CuiHelper.DestroyUi(player, TELEPORT_UI);
                }
            }

            public bool ShowBuyableTeleportUi(BasePlayer player, bool moveUI, double seconds = 0d, string mode = RaidableMode.Random)
            {
                if (player == null || !player.IsConnected)
                {
                    return false;
                }

                UITeleportSettings ui = config.UI.Teleport;
                if (ui == null || !ui.Enabled)
                {
                    DestroyUi(player, UiType.Teleport);
                    return false;
                }

                ulong userid = player.userID;

                if (!Teleport.TryGetValue(userid, out TimeSettings settings))
                {
                    if (seconds <= 0d)
                    {
                        return false;
                    }

                    double duration = Math.Max(1d, seconds);
                    settings = new TimeSettings
                    {
                        time = Time.timeAsDouble + duration,
                        mode = mode
                    };
                    TimeSettings scheduled = settings;
                    settings.Timer = Instance.timer.Once((float)duration, () => ExpireTeleport(userid, scheduled));
                    Teleport[userid] = settings;
                }

                if (settings.time <= Time.timeAsDouble)
                {
                    ExpireTeleport(userid, settings);
                    return false;
                }

                PlayerUi state = TryAddUser(player);
                UiOffsets offsets = GetOffsets(userid, UiType.Teleport);
                int time = Math.Max(0, (int)Math.Ceiling(settings.time - Time.timeAsDouble));
                string timeText = mx("Teleport Seconds To Accept", player.UserIDString, time);
                string modeName = settings.mode ?? RaidableMode.Random;
                bool recreate = !state.IsSameTeleportLayout(offsets, modeName, ui.ShowMoveButton);

                if (recreate)
                {
                    CuiHelper.DestroyUi(player, TELEPORT_UI);
                    state.InvalidateTeleport();

                    UiPalette palette = GetPalette();
                    bool useTheme = ui.UseThemeColors;
                    float panelAlpha = ui.PanelAlpha ?? 0.98f;
                    string background = useTheme ? palette.Background : ConvertHexToRGBA(ui.PanelColor, panelAlpha);
                    string panel = useTheme ? palette.Panel : ConvertHexToRGBA(ui.TitlePanelColor, 1f);
                    string cell = useTheme ? palette.Cell : panel;
                    string accentHex = config.UI.Buyable.Difficulty ? config.Settings.Management.Colors2.Get(modeName) : config.UI.Buyable.GetButton(modeName);
                    string accent = ConvertHexToRGBA(accentHex, 1f);
                    string textColor = useTheme ? palette.Text : ConvertHexToRGBA(config.UI.Buyable.XTextColor, 1f);
                    string muted = useTheme ? palette.Muted : ConvertHexToRGBA(config.UI.Buyable.XTextColor, 0.72f);
                    int fontSize = Mathf.Clamp(ui.FontSize, 9, 20);
                    float width = Mathf.Max(TeleportUiMinimumWidth, offsets.Max.x - offsets.Min.x);
                    float height = Mathf.Max(TeleportUiMinimumHeight, offsets.Max.y - offsets.Min.y);
                    CuiElementContainer container = new();

                    AddMovablePanel(container, TELEPORT_PARENT, TELEPORT_UI, background, "0.5 0", offsets, width, height, true, moveUI, ui.AllowDraggingWithCursor);
                    AddCuiPanel(container, accent, "0 1", "1 1", "0 -2", "0 0", TELEPORT_UI, $"{TELEPORT_UI}_Accent");
                    AddCuiPanel(container, accent, "0 1", "0 1", "10 -24", "16 -18", TELEPORT_UI, $"{TELEPORT_UI}_Dot");
                    AddCuiElement(container, mx("Teleport Question", player.UserIDString), Mathf.Min(22, fontSize + 3), TextAnchor.MiddleLeft, accent,
                        "0 1", "1 1", "24 -34", ui.ShowMoveButton ? "-72 -4" : "-10 -4", TELEPORT_UI, $"{TELEPORT_UI}_Title");

                    if (ui.ShowMoveButton)
                    {
                        AddCuiButton(container, panel, $"rb_ui_move {UiType.Teleport}", mx(moveUI ? "UIStatusDone" : "UIStatusMove", player.UserIDString), moveUI ? accent : muted,
                            Mathf.Max(8, fontSize - 2), TextAnchor.MiddleCenter, "1 1", "1 1", "-66 -33", "-10 -7", TELEPORT_UI, TELEPORT_MOVE_BUTTON, textName: TELEPORT_MOVE_TEXT);
                    }

                    AddCuiPanel(container, cell, "0 0", "1 1", "8 8", "-8 -39", TELEPORT_UI, $"{TELEPORT_UI}_Body");
                    string body = $"{TELEPORT_UI}_Body";
                    AddCuiElement(container, timeText, fontSize, TextAnchor.MiddleCenter, textColor,
                        "0 1", "1 1", "10 -42", "-10 -8", body, TELEPORT_TIME_TEXT, false);
                    AddCuiButton(container, ConvertHexToRGBA(accentHex, config.UI.Buyable.ButtonAlpha), "ui_buyraid accept_teleport", mx("Accept", player.UserIDString),
                        ConvertHexToRGBA(config.UI.Buyable.TypeSelector?.ButtonTextColor ?? "#FFFFFF", 1f), fontSize, TextAnchor.MiddleCenter,
                        "0 0", "0.5 0", "10 10", "-3 42", body, $"{TELEPORT_UI}_Accept");
                    AddCuiButton(container, panel, "ui_buyraid decline_teleport", mx("Decline", player.UserIDString), textColor, fontSize, TextAnchor.MiddleCenter,
                        "0.5 0", "1 0", "3 10", "-10 42", body, $"{TELEPORT_UI}_Decline");

                    if (!CuiHelper.AddUi(player, container))
                    {
                        state.InvalidateTeleport();
                        return false;
                    }

                    state.TeleportUiCreated = true;
                    state.TeleportMoving = moveUI;
                    state.TeleportTimeText = timeText;
                    state.SetTeleportLayout(offsets, modeName, ui.ShowMoveButton);
                }
                else
                {
                    CuiElementContainer updates = new();

                    if (state.TeleportMoving != moveUI)
                    {
                        AddCuiInteractionUpdate(updates, TELEPORT_PARENT, TELEPORT_UI, true, moveUI || ui.AllowDraggingWithCursor);

                        if (ui.ShowMoveButton)
                        {
                            UiPalette palette = GetPalette();
                            string accentHex = config.UI.Buyable.Difficulty ? config.Settings.Management.Colors2.Get(modeName) : config.UI.Buyable.GetButton(modeName);
                            string moveColor = moveUI ? ConvertHexToRGBA(accentHex, 1f) : (ui.UseThemeColors ? palette.Muted : ConvertHexToRGBA(config.UI.Buyable.XTextColor, 0.72f));
                            AddCuiTextUpdate(updates, mx(moveUI ? "UIStatusDone" : "UIStatusMove", player.UserIDString), TELEPORT_MOVE_BUTTON, TELEPORT_MOVE_TEXT, moveColor);
                        }
                    }

                    if (state.TeleportTimeText != timeText)
                    {
                        AddCuiTextUpdate(updates, timeText, $"{TELEPORT_UI}_Body", TELEPORT_TIME_TEXT);
                    }

                    if (updates.Count > 0 && !CuiHelper.AddUi(player, updates))
                    {
                        state.InvalidateTeleport();
                        return false;
                    }

                    state.TeleportMoving = moveUI;
                    state.TeleportTimeText = timeText;
                }

                if (moveUI && !IsMovingUi(player, UiType.Teleport))
                {
                    TrySetMoveUi(player, UiType.Teleport);
                }

                state.TeleportRefresh = Time.realtimeSinceStartupAsDouble + 1d;
                EnsureStatusRefreshCoroutine();
                return true;
            }

            public bool ShowDelayUi(BasePlayer player, bool moveUI)
            {
                if (player == null || !player.IsConnected || player.IsKilled())
                {
                    return false;
                }

                if (!Instance.GetPVPDelay(player.userID, false, out DelaySettings delay))
                {
                    DestroyUi(player, UiType.Delay);
                    return false;
                }

                if (delay.time <= Time.timeAsDouble)
                {
                    Instance.ExpirePVPDelay(player.userID, delay);
                    DestroyUi(player, UiType.Delay);
                    return false;
                }

                PlayerUi state = TryAddUser(player);

                if (Instance.EventTerritory(player.transform.position))
                {
                    CuiHelper.DestroyUi(player, DELAY_UI);
                    state.InvalidateDelay();
                    return true;
                }

                UIDelaySettings settings = config.UI.Delay;
                int seconds = Math.Max(0, (int)Math.Ceiling(delay.time - Time.timeAsDouble));
                string timeText = mx("UIFormatLockoutSeconds", player.UserIDString, seconds);
                bool recreate = !state.DelayUiCreated || state.DelayMoving != moveUI;

                if (recreate)
                {
                    CuiHelper.DestroyUi(player, DELAY_UI);
                    UiPalette palette = GetPalette();
                    bool useTheme = settings.UseThemeColors;
                    string background = useTheme ? palette.Background : ConvertHexToRGBA(settings.PanelColor, settings.PanelAlpha ?? 0.98f);
                    string panel = useTheme ? palette.Panel : ConvertHexToRGBA(settings.TitlePanelColor, 1f);
                    string accent = ConvertHexToRGBA(settings.TextColor, 1f);
                    string text = useTheme ? palette.Text : ConvertHexToRGBA(settings.TextColor, 1f);
                    string muted = useTheme ? palette.Muted : ConvertHexToRGBA(settings.TextColor, 0.72f);
                    int fontSize = Mathf.Clamp(settings.FontSize, 9, 20);
                    CuiElementContainer container = new();
                    UiOffsets offsets = GetOffsets(player.userID, UiType.Delay);

                    AddMovablePanel(container, DELAY_PARENT, DELAY_UI, background, "0.5 0", offsets, DelayUiWidth, DelayUiHeight, moveUI, moveUI, settings.AllowDraggingWithCursor);
                    AddCuiPanel(container, accent, "0 1", "1 1", "0 -2", "0 0", DELAY_UI, $"{DELAY_UI}_Accent");
                    AddCuiPanel(container, accent, "0 0.5", "0 0.5", "10 -3", "16 3", DELAY_UI, $"{DELAY_UI}_Dot");
                    AddCuiElement(container, mx("UIDelayTitle", player.UserIDString), Mathf.Min(22, fontSize + 2), TextAnchor.MiddleLeft, accent,
                        "0 0", "1 1", "24 0", settings.ShowMoveButton ? "-112 0" : "-62 0", DELAY_UI, DELAY_TITLE_TEXT);
                    AddCuiElement(container, timeText, fontSize, TextAnchor.MiddleRight, text,
                        "1 0", "1 1", "-58 0", "-10 0", DELAY_UI, DELAY_TIME_TEXT);

                    if (settings.ShowMoveButton)
                    {
                        AddCuiButton(container, panel, $"rb_ui_move {UiType.Delay}", mx(moveUI ? "UIStatusDone" : "UIStatusMove", player.UserIDString), moveUI ? accent : muted,
                            Mathf.Max(8, fontSize - 2), TextAnchor.MiddleCenter, "1 0.5", "1 0.5", "-108 -12", "-62 12", DELAY_UI, DELAY_MOVE_BUTTON, textName: DELAY_MOVE_TEXT);
                    }

                    if (!CuiHelper.AddUi(player, container))
                    {
                        state.InvalidateDelay();
                        return false;
                    }

                    state.DelayUiCreated = true;
                    state.DelayMoving = moveUI;
                    state.DelayTimeText = timeText;
                }
                else if (state.DelayTimeText != timeText)
                {
                    CuiElementContainer updates = new();
                    AddCuiTextUpdate(updates, timeText, DELAY_UI, DELAY_TIME_TEXT);

                    if (!CuiHelper.AddUi(player, updates))
                    {
                        state.InvalidateDelay();
                        return false;
                    }

                    state.DelayTimeText = timeText;
                }

                if (moveUI && !IsMovingUi(player, UiType.Delay))
                {
                    TrySetMoveUi(player, UiType.Delay);
                }

                return true;
            }

            private bool CreateUi(BasePlayer player, bool moveUI, UiType type, string name, float alpha, string title, string titleColor, string backgroundColor, string titlePanelColor, string titleEmbedColor, UiOffsets offsets, List<(string mode, string text)> modes)
            {
                if (player == null || !player.IsConnected || modes == null || modes.Count == 0)
                {
                    return false;
                }

                bool isLockout = type == UiType.Lockout;
                bool useTheme = isLockout ? config.UI.Lockout.UseThemeColors : config.UI.BuyableCooldowns.UseThemeColors;
                bool showMoveButton = isLockout ? config.UI.Lockout.ShowMoveButton : config.UI.BuyableCooldowns.ShowMoveButton;
                bool allowDragging = isLockout ? config.UI.Lockout.AllowDraggingWithCursor : config.UI.BuyableCooldowns.AllowDraggingWithCursor;
                int fontSize = Mathf.Clamp(isLockout ? config.UI.Lockout.FontSize : config.UI.BuyableCooldowns.FontSize, 9, 20);
                UiPalette palette = GetPalette();
                string background = useTheme ? palette.Background : backgroundColor;
                string panel = useTheme ? palette.Panel : titlePanelColor;
                string cell = useTheme ? palette.Cell : titleEmbedColor;
                string accent = useTheme ? palette.Accent : titleColor;
                string textColor = useTheme ? palette.Text : titleColor;
                string muted = useTheme ? palette.Muted : ConvertHexToRGBA("#A3A8B3", alpha);
                string parent = isLockout ? LOCKOUT_PARENT : COOLDOWN_PARENT;
                float width = 224f;
                float rowHeight = 23f;
                float bodyHeight = modes.Count * rowHeight + 8f;
                float height = 38f + bodyHeight;
                CuiElementContainer container = new();

                CuiHelper.DestroyUi(player, name);
                AddMovablePanel(container, parent, name, background, "1 0.5", offsets, width, height, moveUI, moveUI, allowDragging);
                AddCuiPanel(container, accent, "0 1", "1 1", "0 -2", "0 0", name, $"{name}_Accent");
                AddCuiPanel(container, accent, "0 1", "0 1", "10 -23", "16 -17", name, $"{name}_Dot");
                AddCuiElement(container, title, Mathf.Min(22, fontSize + 2), TextAnchor.MiddleLeft, accent,
                    "0 1", "1 1", "24 -31", showMoveButton ? "-66 -5" : "-10 -5", name, $"{name}_Title");

                if (showMoveButton)
                {
                    AddCuiButton(container, panel, $"rb_ui_move {type}", mx(moveUI ? "UIStatusDone" : "UIStatusMove", player.UserIDString), moveUI ? accent : muted,
                        Mathf.Max(8, fontSize - 2), TextAnchor.MiddleCenter, "1 1", "1 1", "-60 -31", "-10 -8", name, $"{name}_MoveButton");
                }

                AddCuiPanel(container, cell, "0 0", "1 1", "8 8", "-8 -35", name, $"{name}_Body");
                string body = $"{name}_Body";

                for (int i = 0; i < modes.Count; i++)
                {
                    (string mode, string value) = modes[i];
                    float top = -4f - i * rowHeight;
                    float bottom = top - rowHeight;
                    string token = mode.Replace(" ", "_");
                    string modeColor = GetModeColor(mode, 1f);
                    string modeText = rf(mx($"Mode{mode}", player.UserIDString));

                    AddCuiPanel(container, modeColor, "0 1", "0 1",
                        FormattableString.Invariant($"10 {top - 14f:0.###}"), FormattableString.Invariant($"16 {top - 8f:0.###}"), body, $"{name}_{token}_Dot");
                    AddCuiElement(container, modeText, fontSize, TextAnchor.MiddleLeft, textColor,
                        "0 1", "0 1", FormattableString.Invariant($"24 {bottom:0.###}"), FormattableString.Invariant($"150 {top:0.###}"), body, $"{name}_{token}_Mode", false);
                    AddCuiElement(container, value, fontSize, TextAnchor.MiddleRight, textColor,
                        "1 1", "1 1", FormattableString.Invariant($"-72 {bottom:0.###}"), FormattableString.Invariant($"-10 {top:0.###}"), body, $"{name}_{token}_Value");

                    if (i + 1 < modes.Count)
                    {
                        AddCuiPanel(container, palette.Divider, "0 1", "1 1",
                            FormattableString.Invariant($"10 {bottom:0.###}"), FormattableString.Invariant($"-10 {bottom + 1f:0.###}"), body, $"{name}_{token}_Divider");
                    }
                }

                bool added = CuiHelper.AddUi(player, container);
                if (added && moveUI && !IsMovingUi(player, type))
                {
                    TrySetMoveUi(player, type);
                }

                return added;
            }

            public bool ShowLockoutsUi(BasePlayer player, bool moveUI)
            {
                if (player == null || !player.IsConnected || !config.UI.Lockout.Enabled)
                {
                    return false;
                }

                if (!data.Lockouts.TryGetValue(player.UserIDString, out Lockout lockout) || !lockout.Any())
                {
                    CuiHelper.DestroyUi(player, LOCKOUT_UI);
                    return false;
                }

                UILockoutSettings settings = config.UI.Lockout;
                if (settings.BuyOnly && !PublicEvents.Contains(player.userID))
                {
                    CuiHelper.DestroyUi(player, LOCKOUT_UI);
                    return false;
                }

                using var rows = DisposableList<(string mode, string text)>();
                List<string> modes = Instance.GetRaidableModes();

                for (int i = 0; i < modes.Count; i++)
                {
                    string mode = modes[i];
                    if (lockout.Get(mode) <= 0d)
                    {
                        continue;
                    }

                    rows.Add((mode, mx("UIFormatLockoutMinutes", player.UserIDString, GetMinutes(mode, lockout))));
                }

                if (rows.Count == 0)
                {
                    CuiHelper.DestroyUi(player, LOCKOUT_UI);
                    return false;
                }

                return CreateUi(player, moveUI, UiType.Lockout, LOCKOUT_UI, settings.Alpha,
                    mx("Normal Lockouts", player.UserIDString),
                    ConvertHexToRGBA(settings.TitleColor, 1f),
                    ConvertHexToRGBA(settings.BackgroundColor, settings.Alpha),
                    ConvertHexToRGBA(settings.TitlePanelColor, settings.Alpha),
                    ConvertHexToRGBA(settings.TitleEmbedColor, settings.Alpha),
                    GetOffsets(player.userID, UiType.Lockout), rows);
            }

            public bool ShowBuyableCooldownsUi(BasePlayer player, bool moveUI)
            {
                if (player == null || !player.IsConnected || !config.UI.BuyableCooldowns.Enabled)
                {
                    return false;
                }

                if (Instance.RaidableModes.Count == 0 && Instance.IsGridLoading())
                {
                    CuiHelper.DestroyUi(player, COOLDOWN_UI);
                    return false;
                }

                using (var entries = data.BuyableCooldowns.ToPooledList())
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        KeyValuePair<ulong, BuyableInfo> pair = entries[i];
                        if (pair.Key.HasPermission("raidablebases.buyable.bypass.cooldown") || !BuyableInfo.HasTimeRemaining(Instance, pair.Key))
                        {
                            data.BuyableCooldowns.Remove(pair.Key);
                        }
                    }
                }

                if (!data.BuyableCooldowns.ContainsKey(player.userID))
                {
                    CuiHelper.DestroyUi(player, COOLDOWN_UI);
                    return false;
                }

                UICooldownSettings settings = config.UI.BuyableCooldowns;
                if (settings.BuyOnly && !PrivateEvents.Contains(player.userID))
                {
                    CuiHelper.DestroyUi(player, COOLDOWN_UI);
                    return false;
                }

                using var rows = DisposableList<(string mode, string text)>();
                List<string> modes = Instance.GetRaidableModes();

                for (int i = 0; i < modes.Count; i++)
                {
                    string mode = modes[i];
                    double minutes = GetMinutes(player, mode);
                    if (minutes <= 0d)
                    {
                        continue;
                    }

                    rows.Add((mode, mx("UIFormatLockoutMinutes", player.UserIDString, minutes)));
                }

                if (rows.Count == 0)
                {
                    CuiHelper.DestroyUi(player, COOLDOWN_UI);
                    return false;
                }

                return CreateUi(player, moveUI, UiType.Cooldown, COOLDOWN_UI, settings.Alpha,
                    mx("Buyable Cooldowns", player.UserIDString),
                    ConvertHexToRGBA(settings.TitleColor, 1f),
                    ConvertHexToRGBA(settings.BackgroundColor, settings.Alpha),
                    ConvertHexToRGBA(settings.TitlePanelColor, settings.Alpha),
                    ConvertHexToRGBA(settings.TitleEmbedColor, settings.Alpha),
                    GetOffsets(player.userID, UiType.Cooldown), rows);
            }

            public bool ShowPasteProgressUi(BasePlayer player, string filename, float progress, string stage, bool moveUI)
            {
                UIPasteProgressSettings settings = config.UI.PasteProgress;

                if (player == null || !player.IsConnected || settings == null || !settings.Enabled)
                {
                    DestroyUi(player, UiType.PasteProgress);
                    return false;
                }

                PlayerUi state = TryAddUser(player);
                UiOffsets offsets = GetOffsets(player.userID, UiType.PasteProgress);
                progress = Mathf.Clamp01(progress);
                PasteSnapshot snapshot = new(
                    settings.ShowFileName,
                    settings.ShowMoveButton,
                    moveUI,
                    settings.ShowFileName ? filename ?? string.Empty : string.Empty,
                    stage ?? string.Empty,
                    FormattableString.Invariant($"{Mathf.RoundToInt(progress * 100f)}%"),
                    mx(moveUI ? "UIStatusDone" : "UIStatusMove", player.UserIDString),
                    progress);
                bool layoutChanged = !state.IsSamePasteLayout(offsets, snapshot.ShowFileName, snapshot.ShowMoveButton);
                bool sent;

                if (layoutChanged)
                {
                    CuiHelper.DestroyUi(player, PASTE_PROGRESS_UI);
                    state.InvalidatePaste();
                    sent = CreatePasteProgressUi(player, state, offsets, snapshot);
                }
                else
                {
                    sent = UpdatePasteProgressUi(player, state, snapshot);
                }

                if (!sent)
                {
                    return false;
                }

                if (moveUI && !IsMovingUi(player, UiType.PasteProgress))
                {
                    TrySetMoveUi(player, UiType.PasteProgress);
                }

                return true;
            }

            private bool CreatePasteProgressUi(BasePlayer player, PlayerUi state, UiOffsets offsets, PasteSnapshot snapshot)
            {
                UIPasteProgressSettings settings = config.UI.PasteProgress;
                UiPalette palette = GetPalette();
                bool useTheme = settings.UseThemeColors;
                string background = useTheme ? palette.Background : ConvertHexToRGBA(settings.PanelColor, settings.PanelAlpha ?? 0.94f);
                string panel = useTheme ? palette.Panel : ConvertHexToRGBA(settings.TitlePanelColor, 1f);
                string cell = useTheme ? palette.Cell : panel;
                string accent = useTheme ? palette.Accent : ConvertHexToRGBA(settings.ProgressColor, 1f);
                string text = useTheme ? palette.Text : ConvertHexToRGBA(settings.TextColor, 1f);
                string muted = useTheme ? palette.Muted : ConvertHexToRGBA(settings.TextColor, 0.72f);
                int fontSize = Mathf.Clamp(settings.FontSize, 9, 20);
                int titleFontSize = Mathf.Min(22, fontSize + 2);
                int detailFontSize = Mathf.Max(8, fontSize - 2);
                float width = Mathf.Max(PasteProgressUiMinimumWidth, offsets.Max.x - offsets.Min.x);
                float height = Mathf.Max(PasteProgressUiMinimumHeight, offsets.Max.y - offsets.Min.y);
                CuiElementContainer container = new();

                AddMovablePanel(container, STATUS_PARENT, PASTE_PROGRESS_UI, background, "0.5 0.5", offsets, width, height, snapshot.Moving, snapshot.Moving, settings.AllowDraggingWithCursor);

                AddCuiPanel(container, accent, "0 1", "1 1", "0 -2", "0 0", PASTE_PROGRESS_UI, $"{PASTE_PROGRESS_UI}_Accent");
                AddCuiPanel(container, accent, "0 1", "0 1", "10 -23", "16 -17", PASTE_PROGRESS_UI, $"{PASTE_PROGRESS_UI}_Dot");
                AddCuiElement(container, mx("UIPasteProgressTitle", player.UserIDString), titleFontSize, TextAnchor.MiddleLeft, accent, "0 1", "1 1", "24 -31", snapshot.ShowMoveButton ? "-116 -5" : "-58 -5", PASTE_PROGRESS_UI, PASTE_TITLE_TEXT);
                AddCuiElement(container, snapshot.PercentText, fontSize, TextAnchor.MiddleRight, text, "1 1", "1 1", "-56 -31", "-10 -5", PASTE_PROGRESS_UI, PASTE_PERCENT_TEXT);

                if (snapshot.ShowMoveButton)
                {
                    AddCuiButton(container, panel, $"rb_ui_move {UiType.PasteProgress}", snapshot.MoveText, snapshot.Moving ? accent : muted, detailFontSize, TextAnchor.MiddleCenter, "1 1", "1 1", "-110 -31", "-62 -8", PASTE_PROGRESS_UI, PASTE_MOVE_BUTTON, textName: PASTE_MOVE_TEXT);
                }

                AddCuiPanel(container, cell, "0 0", "1 1", "8 8", "-8 -35", PASTE_PROGRESS_UI, PASTE_BODY);

                if (snapshot.ShowFileName)
                {
                    AddCuiElement(container, snapshot.FilenameText, detailFontSize, TextAnchor.MiddleLeft, muted, "0 0.36", "0.46 1", "10 0", "0 0", PASTE_BODY, PASTE_FILENAME_TEXT, false);
                    AddCuiElement(container, snapshot.StageText, detailFontSize, TextAnchor.MiddleRight, muted, "0.38 0.36", "1 1", "0 0", "-10 0", PASTE_BODY, PASTE_STAGE_TEXT, false);
                }
                else
                {
                    AddCuiElement(container, snapshot.StageText, detailFontSize, TextAnchor.MiddleLeft, muted, "0 0.36", "1 1", "10 0", "-10 0", PASTE_BODY, PASTE_STAGE_TEXT, false);
                }

                AddCuiPanel(container, palette.ProgressBackground, "0 0", "1 0", "10 7", "-10 11", PASTE_BODY, PASTE_TRACK);
                AddCuiPanel(container, accent, "0 0", FormattableString.Invariant($"{snapshot.Progress:0.####} 1"), "0 0", "0 0", PASTE_TRACK, PASTE_FILL);

                if (!CuiHelper.AddUi(player, container))
                {
                    state.InvalidatePaste();
                    return false;
                }

                state.PasteUiCreated = true;
                state.SetPasteLayout(offsets, snapshot.ShowFileName, snapshot.ShowMoveButton);
                state.SetPasteSnapshot(snapshot);
                return true;
            }

            private bool UpdatePasteProgressUi(BasePlayer player, PlayerUi state, PasteSnapshot snapshot)
            {
                UIPasteProgressSettings settings = config.UI.PasteProgress;
                CuiElementContainer updates = new();
                PasteSnapshot previous = state.PasteSnapshot;

                if (!state.HasPasteSnapshot || previous.Moving != snapshot.Moving)
                {
                    AddCuiInteractionUpdate(updates, STATUS_PARENT, PASTE_PROGRESS_UI, snapshot.Moving, snapshot.Moving || settings.AllowDraggingWithCursor);
                }

                if (snapshot.ShowFileName && (!state.HasPasteSnapshot || previous.FilenameText != snapshot.FilenameText))
                {
                    AddCuiTextUpdate(updates, snapshot.FilenameText, PASTE_BODY, PASTE_FILENAME_TEXT);
                }

                if (!state.HasPasteSnapshot || previous.StageText != snapshot.StageText)
                {
                    AddCuiTextUpdate(updates, snapshot.StageText, PASTE_BODY, PASTE_STAGE_TEXT);
                }

                if (!state.HasPasteSnapshot || previous.PercentText != snapshot.PercentText)
                {
                    AddCuiTextUpdate(updates, snapshot.PercentText, PASTE_PROGRESS_UI, PASTE_PERCENT_TEXT);
                }

                if (snapshot.ShowMoveButton && (!state.HasPasteSnapshot || previous.MoveText != snapshot.MoveText || previous.Moving != snapshot.Moving))
                {
                    UiPalette palette = GetPalette();
                    string moveColor = settings.UseThemeColors ? (snapshot.Moving ? palette.Accent : palette.Muted) : ConvertHexToRGBA(snapshot.Moving ? settings.ProgressColor : settings.TextColor, snapshot.Moving ? 1f : 0.72f);
                    AddCuiTextUpdate(updates, snapshot.MoveText, PASTE_MOVE_BUTTON, PASTE_MOVE_TEXT, moveColor);
                }

                if (!state.HasPasteSnapshot || !Mathf.Approximately(previous.Progress, snapshot.Progress))
                {
                    AddCuiRectTransformUpdate(updates, FormattableString.Invariant($"{snapshot.Progress:0.####} 1"), PASTE_TRACK, PASTE_FILL);
                }

                if (updates.Count > 0 && !CuiHelper.AddUi(player, updates))
                {
                    state.InvalidatePaste();
                    return false;
                }

                state.SetPasteSnapshot(snapshot);
                return true;
            }

            private static double GetMinutes(double seconds)
            {
                return Math.Ceiling(TimeSpan.FromSeconds(seconds).TotalMinutes);
            }

            private double GetMinutes(BasePlayer buyer, string mode)
            {
                return GetMinutes(Math.Max(0d, BuyableInfo.GetTimeRemaining(Instance, buyer, mode, false)));
            }

            private string GetMinutes(string mode, Lockout lockout)
            {
                return GetMinutes(Math.Max(0d, lockout.Get(mode))).ToString(CultureInfo.InvariantCulture);
            }

            private string GetModeColor(string mode, float alpha)
            {
                string color = config.UI.Buyable.Difficulty ? config.Settings.Management.Colors2.Get(mode) : config.UI.Buyable.GetButton(mode);
                return ConvertHexToRGBA(color, alpha);
            }

            public float GetBoundSize(BaseEntity entity)
            {
                return entity == null ? 0f : entity.bounds.size.Max();
            }

            public bool ShowStatusUi(BasePlayer player, RaidableBase raid, bool moveUI)
            {
                return RefreshStatusUi(player, raid, moveUI, false);
            }

            private bool RefreshStatusUi(BasePlayer player, RaidableBase raid, bool moveUI, bool force)
            {
                if (player == null || !player.IsConnected || raid == null || raid.IsDespawning || !config.UI.Status.Enabled)
                {
                    DestroyUi(player, UiType.Status);
                    return false;
                }

                PlayerUi state = TryAddUser(player);
                UiOffsets offsets = GetOffsets(player.userID, UiType.Status);
                StatusSnapshot snapshot = CreateStatusSnapshot(player, raid, moveUI);
                double now = Time.realtimeSinceStartupAsDouble;
                bool layoutChanged = !state.StatusUiCreated || !state.IsSameLayout(offsets, snapshot.ShowLoot, snapshot.UseRows, snapshot.ShowMoveButton);
                bool raidChanged = state.StatusRaid != raid;

                if (!layoutChanged && !raidChanged && !force && state.Status > now)
                {
                    return true;
                }

                bool sent;

                if (layoutChanged)
                {
                    CuiHelper.DestroyUi(player, STATUS_UI);
                    state.InvalidateStatus();
                    sent = CreateStatusUi(player, state, offsets, moveUI, snapshot);
                }
                else
                {
                    sent = UpdateStatusUi(player, state, snapshot);
                }

                if (!sent)
                {
                    state.Status = 0d;
                    return false;
                }

                state.StatusRaid = raid;
                state.Status = now + STATUS_REFRESH_INTERVAL;
                EnsureStatusRefreshCoroutine();

                if (moveUI && !IsMovingUi(player, UiType.Status))
                {
                    TrySetMoveUi(player, UiType.Status);
                }

                return true;
            }

            private StatusSnapshot CreateStatusSnapshot(BasePlayer player, RaidableBase raid, bool moveUI)
            {
                UIStatusSettings settings = config.UI.Status;
                string eventType = raid.AllowPVP
                    ? mx(raid.Options.Eco.Enabled ? "PVP ECO UI" : "PVP UI", player.UserIDString)
                    : mx(raid.Options.Eco.Enabled ? "PVE ECO UI" : "PVE UI", player.UserIDString);
                string modeColor = ConvertHexToRGBA(raid.AllowPVP ? settings.ColorPVP : settings.ColorPVE, 1f);
                string timerText = settings.ShowDespawnTime ? FormatStatusTime(raid.DespawnSecondsRemaining) : string.Empty;
                bool showLoot = settings.ShowLootLeft && (!settings.HideWithoutOwner || raid.ownerId.IsSteamId());

                SetOwner(raid, settings, player, out string ownerName, out string ownerColor);

                string stateText = mx(raid.IsOpened ? "UIStatusActive" : "UIStatusComplete", player.UserIDString);
                bool useRows = !string.Equals(settings.ContentLayout, STATUS_LAYOUT_CARDS, StringComparison.OrdinalIgnoreCase);
                string subtitleText = settings.ShowRaidDetails
                    ? FormatRaidDetails(settings.RaidDetailsFormat, raid.Type.ToString(), raid.LangMode(player.UserIDString, true), rf(eventType), stateText)
                    : string.Empty;
                string primaryName = raid.IsOpened ? mx("Owner", player.UserIDString) : mx("UIStatusState", player.UserIDString);
                string primaryLabel = useRows ? FormatStatusRowLabel(primaryName) : FormatStatusLabel(primaryName);
                string primaryValue = raid.IsOpened ? ownerName : mx("UIStatusCompleted", player.UserIDString);
                string lootName = useRows ? FormatStatusRowLabel(mx("Loot", player.UserIDString)) : FormatStatusLabel(mx("Loot", player.UserIDString));
                string remainingText = mx("UIStatusRemaining", player.UserIDString);
                string lootLabel = showLoot ? $"{lootName} {remainingText}" : string.Empty;

                return new(
                    showLoot,
                    useRows,
                    settings.ShowMoveButton,
                    moveUI,
                    mx("UIStatusTitle", player.UserIDString),
                    subtitleText,
                    timerText,
                    mx(moveUI ? "UIStatusDone" : "UIStatusMove", player.UserIDString),
                    primaryLabel,
                    primaryValue,
                    lootLabel,
                    showLoot ? raid.GetLootAmountCounted().ToString(CultureInfo.InvariantCulture) : string.Empty,
                    modeColor,
                    raid.IsOpened ? ownerColor : ConvertHexToRGBA(settings.PositiveColor, 1f));
            }

            private static string FormatStatusLabel(string value)
            {
                return string.IsNullOrWhiteSpace(value) ? string.Empty : rf(value).Trim().TrimEnd(':').ToLowerInvariant();
            }

            private static string FormatStatusRowLabel(string value)
            {
                return string.IsNullOrWhiteSpace(value) ? string.Empty : rf(value).Trim().TrimEnd(':');
            }

            private static string FormatRaidDetails(string format, string type, string difficulty, string mode, string state)
            {
                if (string.IsNullOrEmpty(format))
                {
                    return string.Empty;
                }

                using var builder = DisposableBuilder.Get();

                for (int index = 0; index < format.Length;)
                {
                    if (format[index] == '{')
                    {
                        if (index + 6 <= format.Length && string.CompareOrdinal(format, index, "{type}", 0, 6) == 0)
                        {
                            builder.Append(type);
                            index += 6;
                            continue;
                        }

                        if (index + 12 <= format.Length && string.CompareOrdinal(format, index, "{difficulty}", 0, 12) == 0)
                        {
                            builder.Append(difficulty);
                            index += 12;
                            continue;
                        }

                        if (index + 6 <= format.Length && string.CompareOrdinal(format, index, "{mode}", 0, 6) == 0)
                        {
                            builder.Append(mode);
                            index += 6;
                            continue;
                        }

                        if (index + 7 <= format.Length && string.CompareOrdinal(format, index, "{state}", 0, 7) == 0)
                        {
                            builder.Append(state);
                            index += 7;
                            continue;
                        }
                    }

                    builder.Append(format[index++]);
                }

                return builder.ToString();
            }

            private static string FormatStatusTime(double seconds)
            {
                if (seconds <= 0d)
                {
                    return string.Empty;
                }

                TimeSpan remaining = TimeSpan.FromSeconds(Math.Ceiling(seconds));
                return remaining.TotalHours >= 1d
                    ? FormattableString.Invariant($"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}")
                    : FormattableString.Invariant($"{remaining.Minutes:00}:{remaining.Seconds:00}");
            }

            private bool CreateStatusUi(BasePlayer player, PlayerUi state, UiOffsets offsets, bool moveUI, StatusSnapshot snapshot)
            {
                UIStatusSettings settings = config.UI.Status;
                UiPalette palette = GetPalette();
                bool useTheme = settings.UseThemeColors;
                float panelAlpha = settings.PanelAlpha ?? 0.98f;
                string background = useTheme ? palette.Background : ConvertHexToRGBA(settings.PanelColor, panelAlpha);
                string panel = useTheme ? palette.Panel : ConvertHexToRGBA(settings.TitlePanelColor, 1f);
                string cell = useTheme ? palette.Cell : ConvertHexToRGBA(settings.TitlePanelColor, 1f);
                string titleColor = useTheme ? palette.Accent : snapshot.ModeColor;
                int fontSize = Mathf.Clamp(settings.FontSize, 9, 20);
                int titleFontSize = Mathf.Min(22, fontSize + 2);
                int valueFontSize = Mathf.Min(22, fontSize + 2);
                int labelFontSize = Mathf.Max(8, fontSize - 2);
                float width = Mathf.Max(StatusUiMinimumWidth, offsets.Max.x - offsets.Min.x);
                float height = Mathf.Max(StatusUiMinimumHeight, offsets.Max.y - offsets.Min.y);
                CuiElementContainer container = new();

                AddMovablePanel(container, STATUS_PARENT, STATUS_UI, background, "0.5 0", offsets, width, height, moveUI, moveUI, settings.AllowDraggingWithCursor);

                AddCuiPanel(container, snapshot.ModeColor, "0 1", "0 1", "10 -22", "16 -16", STATUS_UI, STATUS_MODE_DOT);
                AddCuiElement(container, snapshot.TitleText, titleFontSize, TextAnchor.MiddleLeft, titleColor,
                    "0 1", "0.58 1", "24 -29", "0 -5", STATUS_UI, STATUS_TITLE_TEXT);
                AddCuiElement(container, snapshot.SubtitleText, labelFontSize, TextAnchor.MiddleLeft, palette.Muted,
                    "0 1", snapshot.ShowMoveButton ? "0.72 1" : "1 1", "10 -43", snapshot.ShowMoveButton ? "0 -27" : "-10 -27", STATUS_UI, STATUS_SUBTITLE_TEXT, false);
                AddCuiElement(container, snapshot.TimerText, fontSize, TextAnchor.MiddleRight, palette.Text,
                    "0.62 1", "1 1", "0 -28", "-10 -5", STATUS_UI, STATUS_TIMER_TEXT);

                if (snapshot.ShowMoveButton)
                {
                    AddCuiButton(container, panel, $"rb_ui_move {UiType.Status}", snapshot.MoveText, moveUI ? snapshot.ModeColor : palette.Muted,
                        labelFontSize, TextAnchor.MiddleCenter, "1 1", "1 1", "-54 -43", "-10 -26", STATUS_UI, STATUS_MOVE_BUTTON, textName: STATUS_MOVE_TEXT);
                }

                AddCuiPanel(container, cell, "0 0", "1 1", "8 8", "-8 -48", STATUS_UI, STATUS_BODY);

                if (snapshot.UseRows)
                {
                    if (snapshot.ShowLoot)
                    {
                        AddCuiPanel(container, cell, "0 0.5", "1 1", "0 0", "0 0", STATUS_BODY, STATUS_PRIMARY_CARD);
                        AddCuiPanel(container, palette.Divider, "0 0.5", "1 0.5", "10 -0.5", "-10 0.5", STATUS_BODY, $"{STATUS_BODY}_Divider");
                        AddCuiPanel(container, cell, "0 0", "1 0.5", "0 0", "0 0", STATUS_BODY, STATUS_LOOT_CARD);
                    }
                    else
                    {
                        AddCuiPanel(container, cell, "0 0", "1 1", "0 0", "0 0", STATUS_BODY, STATUS_PRIMARY_CARD);
                    }

                    AddCuiElement(container, snapshot.PrimaryLabel, labelFontSize, TextAnchor.MiddleLeft, palette.Muted,
                        "0 0", "0.62 1", "10 0", "0 0", STATUS_PRIMARY_CARD, STATUS_PRIMARY_LABEL, false);
                    AddCuiElement(container, snapshot.PrimaryValue, fontSize, TextAnchor.MiddleRight, snapshot.PrimaryColor,
                        "0.38 0", "1 1", "0 0", "-10 0", STATUS_PRIMARY_CARD, STATUS_PRIMARY_VALUE);

                    if (snapshot.ShowLoot)
                    {
                        AddCuiElement(container, snapshot.LootLabel, labelFontSize, TextAnchor.MiddleLeft, palette.Muted,
                            "0 0", "0.62 1", "10 0", "0 0", STATUS_LOOT_CARD, STATUS_LOOT_LABEL, false);
                        AddCuiElement(container, snapshot.LootValue, fontSize, TextAnchor.MiddleRight, palette.Text,
                            "0.38 0", "1 1", "0 0", "-10 0", STATUS_LOOT_CARD, STATUS_LOOT_VALUE);
                    }
                }
                else
                {
                    if (snapshot.ShowLoot)
                    {
                        AddCuiPanel(container, cell, "0 0", "0.5 1", "0 0", "0 0", STATUS_BODY, STATUS_PRIMARY_CARD);
                        AddCuiPanel(container, palette.Divider, "0.5 0", "0.5 1", "-0.5 0", "0.5 0", STATUS_BODY, $"{STATUS_BODY}_Divider");
                        AddCuiPanel(container, cell, "0.5 0", "1 1", "0 0", "0 0", STATUS_BODY, STATUS_LOOT_CARD);
                    }
                    else
                    {
                        AddCuiPanel(container, cell, "0 0", "1 1", "0 0", "0 0", STATUS_BODY, STATUS_PRIMARY_CARD);
                    }

                    AddCuiElement(container, snapshot.PrimaryValue, valueFontSize, TextAnchor.MiddleLeft, snapshot.PrimaryColor,
                        "0 1", "1 1", "12 -23", "-10 -2", STATUS_PRIMARY_CARD, STATUS_PRIMARY_VALUE);
                    AddCuiElement(container, snapshot.PrimaryLabel, labelFontSize, TextAnchor.MiddleLeft, palette.Muted,
                        "0 0", "1 0", "12 2", "-10 16", STATUS_PRIMARY_CARD, STATUS_PRIMARY_LABEL, false);

                    if (snapshot.ShowLoot)
                    {
                        AddCuiElement(container, snapshot.LootValue, valueFontSize, TextAnchor.MiddleLeft, palette.Text,
                            "0 1", "1 1", "12 -23", "-10 -2", STATUS_LOOT_CARD, STATUS_LOOT_VALUE);
                        AddCuiElement(container, snapshot.LootLabel, labelFontSize, TextAnchor.MiddleLeft, palette.Muted,
                            "0 0", "1 0", "12 2", "-10 16", STATUS_LOOT_CARD, STATUS_LOOT_LABEL, false);
                    }
                }

                if (!CuiHelper.AddUi(player, container))
                {
                    state.InvalidateStatus();
                    return false;
                }

                state.StatusUiCreated = true;
                state.SetLayout(offsets, snapshot.ShowLoot, snapshot.UseRows, snapshot.ShowMoveButton);
                state.SetSnapshot(snapshot);
                return true;
            }

            private bool UpdateStatusUi(BasePlayer player, PlayerUi state, StatusSnapshot snapshot)
            {
                CuiElementContainer updates = new();
                StatusSnapshot previous = state.Snapshot;
                UIStatusSettings settings = config.UI.Status;
                bool useTheme = settings.UseThemeColors;

                if (!state.HasSnapshot || previous.Moving != snapshot.Moving)
                {
                    AddCuiInteractionUpdate(updates, STATUS_PARENT, STATUS_UI, snapshot.Moving, snapshot.Moving || settings.AllowDraggingWithCursor);
                }

                if (!state.HasSnapshot || previous.TitleText != snapshot.TitleText || (!useTheme && previous.ModeColor != snapshot.ModeColor))
                {
                    AddCuiTextUpdate(updates, snapshot.TitleText, STATUS_UI, STATUS_TITLE_TEXT, useTheme ? null : snapshot.ModeColor);
                }

                if (!state.HasSnapshot || previous.ModeColor != snapshot.ModeColor)
                {
                    AddCuiImageUpdate(updates, snapshot.ModeColor, STATUS_UI, STATUS_MODE_DOT);
                }

                if (!state.HasSnapshot || previous.SubtitleText != snapshot.SubtitleText)
                {
                    AddCuiTextUpdate(updates, snapshot.SubtitleText, STATUS_UI, STATUS_SUBTITLE_TEXT);
                }

                if (!state.HasSnapshot || previous.TimerText != snapshot.TimerText)
                {
                    AddCuiTextUpdate(updates, snapshot.TimerText, STATUS_UI, STATUS_TIMER_TEXT);
                }

                if (snapshot.ShowMoveButton && (!state.HasSnapshot || previous.MoveText != snapshot.MoveText || previous.Moving != snapshot.Moving || previous.ModeColor != snapshot.ModeColor))
                {
                    AddCuiTextUpdate(updates, snapshot.MoveText, STATUS_MOVE_BUTTON, STATUS_MOVE_TEXT, snapshot.Moving ? snapshot.ModeColor : GetPalette().Muted);
                }

                if (!state.HasSnapshot || previous.PrimaryLabel != snapshot.PrimaryLabel)
                {
                    AddCuiTextUpdate(updates, snapshot.PrimaryLabel, STATUS_PRIMARY_CARD, STATUS_PRIMARY_LABEL);
                }

                if (!state.HasSnapshot || previous.PrimaryValue != snapshot.PrimaryValue || previous.PrimaryColor != snapshot.PrimaryColor)
                {
                    AddCuiTextUpdate(updates, snapshot.PrimaryValue, STATUS_PRIMARY_CARD, STATUS_PRIMARY_VALUE, snapshot.PrimaryColor);
                }

                if (snapshot.ShowLoot && (!state.HasSnapshot || previous.LootLabel != snapshot.LootLabel))
                {
                    AddCuiTextUpdate(updates, snapshot.LootLabel, STATUS_LOOT_CARD, STATUS_LOOT_LABEL);
                }

                if (snapshot.ShowLoot && (!state.HasSnapshot || previous.LootValue != snapshot.LootValue))
                {
                    AddCuiTextUpdate(updates, snapshot.LootValue, STATUS_LOOT_CARD, STATUS_LOOT_VALUE);
                }

                if (updates.Count > 0 && !CuiHelper.AddUi(player, updates))
                {
                    state.InvalidateStatus();
                    return false;
                }

                state.SetSnapshot(snapshot);
                return true;
            }

            private void SetOwner(RaidableBase raid, UIStatusSettings settings, BasePlayer player, out string ownerName, out string ownerColor)
            {
                ownerColor = settings.NoneColor;
                ownerName = mx("None", player.UserIDString);
                bool exists = raid.raiders.TryGetValue(player.userID, out Raider viewer);

                if (raid.ownerId.IsSteamId())
                {
                    if (raid.ownerId == player.userID)
                    {
                        ownerColor = settings.PositiveColor;
                        ownerName = mx("You", player.UserIDString);
                    }
                    else if ((exists && viewer.IsAlly) || raid.IsAlly(player, raid.ownerId))
                    {
                        (exists ? viewer : raid.GetRaider(player)).IsAlly = true;
                        ownerColor = settings.PositiveColor;
                        ownerName = mx("Ally", player.UserIDString);
                    }
                    else
                    {
                        ownerColor = settings.NegativeColor;
                        ownerName = mx("Enemy", player.UserIDString);
                    }
                }

                if (raid.TryGetOwnerActivityTimeLeft(out double secondsLeft))
                {
                    string minutes = Math.Ceiling(TimeSpan.FromSeconds(secondsLeft).TotalMinutes).ToString(CultureInfo.InvariantCulture);
                    ownerName = $"{ownerName} ({mx("UiInactiveTimeLeft", player.UserIDString, minutes)})";
                }

                ownerColor = ConvertHexToRGBA(ownerColor, 1f);
            }

            private void EnsureStatusRefreshCoroutine()
            {
                if (statusRefreshCoroutine == null && !Instance.IsUnloading && HasPendingStatusRefresh())
                {
                    statusRefreshCoroutine = ServerMgr.Instance.StartCoroutine(StatusRefreshCoroutine());
                }
            }

            private bool HasPendingStatusRefresh()
            {
                foreach (PlayerUi state in users.Values)
                {
                    if (state?.HasRefresh == true)
                    {
                        return true;
                    }
                }

                return false;
            }

            private IEnumerator StatusRefreshCoroutine()
            {
                WaitForSeconds instruction = CoroutineEx.waitForSeconds(0.1f);
                FrameDeadline deadline = new(uiFrameBudgetMilliseconds);

                try
                {
                    while (!Instance.IsUnloading)
                    {
                        double now = Time.realtimeSinceStartupAsDouble;
                        using var snapshot = users.ToPooledList();
                        using var remove = DisposableList<ulong>();

                        foreach (KeyValuePair<ulong, PlayerUi> pair in snapshot)
                        {
                            if (deadline.Expired)
                            {
                                yield return null;
                                deadline.Reset();
                            }

                            if (!users.TryGetValue(pair.Key, out PlayerUi state) || !ReferenceEquals(state, pair.Value))
                            {
                                continue;
                            }

                            if (!state.IsConnected)
                            {
                                remove.Add(pair.Key);
                                continue;
                            }

                            bool retryCooldown = state.Cooldown > 0d && now >= state.Cooldown;
                            bool retryDelay = state.Delay > 0d && now >= state.Delay;
                            bool retryLockout = state.Lockout > 0d && now >= state.Lockout;
                            bool retryTeleport = state.TeleportRefresh > 0d && now >= state.TeleportRefresh;
                            bool retryStatus = state.Status > 0d && now >= state.Status;

                            try
                            {
                                if (retryCooldown)
                                {
                                    state.Cooldown = 0d;
                                    UpdateUi(state.player, UiType.Cooldown);
                                    retryCooldown = false;
                                }

                                if (retryDelay)
                                {
                                    state.Delay = 0d;
                                    UpdateUi(state.player, UiType.Delay);
                                    retryDelay = false;
                                }

                                if (retryLockout)
                                {
                                    state.Lockout = 0d;
                                    UpdateUi(state.player, UiType.Lockout);
                                    retryLockout = false;
                                }

                                if (retryTeleport)
                                {
                                    state.TeleportRefresh = 0d;
                                    UpdateUi(state.player, UiType.Teleport);
                                    retryTeleport = false;
                                }

                                if (retryStatus)
                                {
                                    state.Status = 0d;

                                    if (state.StatusRaid == null || state.StatusRaid.IsDespawning || !Instance.Get(state.player.transform.position, out RaidableBase raid) || raid != state.StatusRaid)
                                    {
                                        DestroyUi(state.player, UiType.Status);
                                    }
                                    else
                                    {
                                        RefreshStatusUi(state.player, raid, IsMovingUi(state.player, UiType.Status), true);
                                    }

                                    retryStatus = false;
                                }
                            }
                            catch (Exception ex)
                            {
                                Instance.QueueExceptionMessage($"{nameof(StatusRefreshCoroutine)} UI ERROR for {state.userid}: {ex}");
                                double retry = Time.realtimeSinceStartupAsDouble + 1d;
                                if (retryCooldown) state.Cooldown = retry;
                                if (retryDelay) state.Delay = retry;
                                if (retryLockout) state.Lockout = retry;
                                if (retryStatus) state.Status = retry;
                                if (retryTeleport) state.TeleportRefresh = retry;
                            }
                        }

                        for (int i = 0; i < remove.Count; i++)
                        {
                            RemoveUser(remove[i]);
                        }

                        if (!HasPendingStatusRefresh())
                        {
                            break;
                        }

                        yield return instruction;
                        deadline.Reset();
                    }
                }
                finally
                {
                    statusRefreshCoroutine = null;

                    if (!Instance.IsUnloading && HasPendingStatusRefresh())
                    {
                        HarmonyModInterface.Mods.NextTick(EnsureStatusRefreshCoroutine);
                    }
                }
            }

            public void TrySetMoveUi(BasePlayer player, UiType type, bool destroyingUi = false)
            {
                ulong userid = player?.userID ?? 0uL;

                if (userid == 0uL || !IsMovableUi(type))
                {
                    if (userid != 0uL)
                    {
                        DestroyTimer(player, userid, type, false);
                    }
                    return;
                }

                if (destroyingUi)
                {
                    DestroyTimer(player, userid, type, false);
                    return;
                }

                if (!Movers.TryGetValue(userid, out Dictionary<UiType, Timer> types))
                {
                    Movers[userid] = types = new();
                }

                if (types.TryGetValue(type, out Timer existing) && existing is { Destroyed: false })
                {
                    existing.Reset();
                    return;
                }

                Timer current = null;
                float seconds = Mathf.Max(1f, GetMoveModeSeconds(type));
                current = Instance.timer.Once(seconds, () =>
                {
                    if (!Movers.TryGetValue(userid, out Dictionary<UiType, Timer> currentTypes) || !currentTypes.TryGetValue(type, out Timer timer) || !ReferenceEquals(timer, current))
                    {
                        return;
                    }

                    currentTypes.Remove(type);
                    if (currentTypes.Count == 0)
                    {
                        Movers.Remove(userid);
                    }

                    if (player != null && player.IsConnected)
                    {
                        UpdateUi(player, type);
                    }
                });
                types[type] = current;
            }

            public void DestroyTimer(BasePlayer player, ulong userid, UiType type, bool update = false)
            {
                if (!Movers.TryGetValue(userid, out Dictionary<UiType, Timer> types) || !types.Remove(type, out Timer closer))
                {
                    return;
                }

                if (types.Count == 0)
                {
                    Movers.Remove(userid);

                    if (Instance.SaveOffsetDataTimer is { Destroyed: false })
                    {
                        Instance.SaveOffsetDataTimer.Reset();
                    }
                }

                if (closer is { Destroyed: false })
                {
                    closer.Destroy();
                }

                if (update && player != null && player.IsConnected)
                {
                    UpdateUi(player, type);
                }
            }

            public bool IsMovingUi(BasePlayer player, UiType type)
            {
                return IsMovableUi(type) && player != null && Movers.TryGetValue(player.userID, out Dictionary<UiType, Timer> types) && types.TryGetValue(type, out Timer timer) && timer is { Destroyed: false };
            }

            private PlayerUi TryAddUser(BasePlayer player)
            {
                if (!Offsets.TryGetValue(player.userID, out Dictionary<UiType, UiOffsets> collection) || collection == null)
                {
                    Offsets[player.userID] = collection = new();
                }

                ValidateAllOffsets(collection);

                if (!users.TryGetValue(player.userID, out PlayerUi state) || state == null)
                {
                    users[player.userID] = state = new();
                }

                state.player = player;
                state.userid = player.userID;
                return state;
            }

            public UiOffsets GetOffsets(ulong userid, UiType type)
            {
                if (!IsMovableUi(type))
                {
                    return GetDefaultOffsets(type).Clone();
                }

                if (!Offsets.TryGetValue(userid, out Dictionary<UiType, UiOffsets> collection) || collection == null)
                {
                    Offsets[userid] = collection = new();
                }

                ValidateAllOffsets(collection);
                return collection[type];
            }

            public void RecordUiPosition(BasePlayer player, UiType type)
            {
                if (player == null || !IsMovableUi(type) || !Offsets.TryGetValue(player.userID, out Dictionary<UiType, UiOffsets> collection) || !collection.TryGetValue(type, out UiOffsets offsets) || !users.TryGetValue(player.userID, out PlayerUi state))
                {
                    return;
                }

                if (type == UiType.Status && state.StatusUiCreated)
                {
                    state.SetPosition(offsets);
                }
                else if (type == UiType.PasteProgress && state.PasteUiCreated)
                {
                    state.SetPastePosition(offsets);
                }
                else if (type == UiType.Teleport && state.TeleportUiCreated)
                {
                    state.SetTeleportPosition(offsets);
                }
            }

            private Dictionary<UiType, UiOffsets> ValidateAllOffsets(Dictionary<UiType, UiOffsets> collection)
            {
                ValidateOffset(collection, UiType.Buyable, DefaultBuyableOffsets);
                ValidateOffset(collection, UiType.Cooldown, DefaulCooldownOffsets);
                ValidateOffset(collection, UiType.Delay, DefaultDelayOffsets);
                ValidateOffset(collection, UiType.Lockout, DefaultLockoutOffsets);
                ValidateOffset(collection, UiType.PasteProgress, DefaultPasteProgressOffsets);
                ValidateOffset(collection, UiType.Status, DefaultStatusOffsets);
                ValidateOffset(collection, UiType.Teleport, DefaultTeleportOffsets);
                return collection;
            }

            private static void ValidateOffset(Dictionary<UiType, UiOffsets> collection, UiType type, UiOffsets defaults)
            {
                if (!collection.TryGetValue(type, out UiOffsets offsets) || !IsValidOffset(offsets))
                {
                    collection[type] = defaults.Clone();
                }
            }

            private UiOffsets GetDefaultOffsets(UiType type)
            {
                return type switch
                {
                    UiType.Buyable => DefaultBuyableOffsets,
                    UiType.Cooldown => DefaulCooldownOffsets,
                    UiType.Delay => DefaultDelayOffsets,
                    UiType.Lockout => DefaultLockoutOffsets,
                    UiType.PasteProgress => DefaultPasteProgressOffsets,
                    UiType.Status => DefaultStatusOffsets,
                    UiType.Teleport => DefaultTeleportOffsets,
                    _ => DefaultStatusOffsets
                };
            }

            public void SaveOffsetData()
            {
                UiDataFile stored = new()
                {
                    Version = UI_DATA_VERSION,
                    LayoutVersions = new()
                    {
                        Buyable = BUYABLE_LAYOUT_VERSION,
                        Cooldown = COOLDOWN_LAYOUT_VERSION,
                        Delay = DELAY_LAYOUT_VERSION,
                        Lockout = LOCKOUT_LAYOUT_VERSION,
                        PasteProgress = PASTE_LAYOUT_VERSION,
                        Status = STATUS_LAYOUT_VERSION,
                        Teleport = TELEPORT_LAYOUT_VERSION
                    }
                };

                foreach (KeyValuePair<ulong, Dictionary<UiType, UiOffsets>> pair in Offsets)
                {
                    if (pair.Value == null)
                    {
                        continue;
                    }

                    UiPlayerData playerData = null;
                    TryStoreOffset(pair.Value, UiType.Buyable, DefaultBuyableOffsets, ref playerData);
                    TryStoreOffset(pair.Value, UiType.Cooldown, DefaulCooldownOffsets, ref playerData);
                    TryStoreOffset(pair.Value, UiType.Delay, DefaultDelayOffsets, ref playerData);
                    TryStoreOffset(pair.Value, UiType.Lockout, DefaultLockoutOffsets, ref playerData);
                    TryStoreOffset(pair.Value, UiType.PasteProgress, DefaultPasteProgressOffsets, ref playerData);
                    TryStoreOffset(pair.Value, UiType.Status, DefaultStatusOffsets, ref playerData);
                    TryStoreOffset(pair.Value, UiType.Teleport, DefaultTeleportOffsets, ref playerData);

                    if (playerData != null)
                    {
                        stored.Players[pair.Key] = playerData;
                    }
                }

                HarmonyDataLayer.WriteObject(Name + "UI", stored);
            }

            private static void TryStoreOffset(Dictionary<UiType, UiOffsets> collection, UiType type, UiOffsets defaults, ref UiPlayerData playerData)
            {
                if (!collection.TryGetValue(type, out UiOffsets offsets) || !IsValidOffset(offsets) || offsets.Equals(defaults))
                {
                    return;
                }

                UiPlayerData data = playerData ??= new();
                UiOffsets stored = offsets.Clone();

                switch (type)
                {
                    case UiType.Buyable: data.Buyable = stored; break;
                    case UiType.Cooldown: data.Cooldown = stored; break;
                    case UiType.Delay: data.Delay = stored; break;
                    case UiType.Lockout: data.Lockout = stored; break;
                    case UiType.PasteProgress: data.PasteProgress = stored; break;
                    case UiType.Status: data.Status = stored; break;
                    case UiType.Teleport: data.Teleport = stored; break;
                }
            }

            public void LoadOffsetData()
            {
                Offsets = new();
                DefaultBuyableOffsets = new(config.UI.Buyable.OffsetMin, config.UI.Buyable.OffsetMax) { NormalizedAnchor = config.UI.Buyable.NormalizedAnchor };
                DefaulCooldownOffsets = new(config.UI.BuyableCooldowns.OffsetMin, config.UI.BuyableCooldowns.OffsetMax) { NormalizedAnchor = config.UI.BuyableCooldowns.NormalizedAnchor };
                DefaultDelayOffsets = new(config.UI.Delay.OffsetMin, config.UI.Delay.OffsetMax) { NormalizedAnchor = config.UI.Delay.NormalizedAnchor };
                DefaultLockoutOffsets = new(config.UI.Lockout.OffsetMin, config.UI.Lockout.OffsetMax) { NormalizedAnchor = config.UI.Lockout.NormalizedAnchor };
                DefaultPasteProgressOffsets = new(config.UI.PasteProgress.OffsetMin, config.UI.PasteProgress.OffsetMax) { NormalizedAnchor = config.UI.PasteProgress.NormalizedAnchor };
                DefaultStatusOffsets = new(config.UI.Status.OffsetMin, config.UI.Status.OffsetMax) { NormalizedAnchor = config.UI.Status.NormalizedAnchor };
                DefaultTeleportOffsets = new(config.UI.Teleport.OffsetMin, config.UI.Teleport.OffsetMax) { NormalizedAnchor = config.UI.Teleport.NormalizedAnchor };
                bool rewriteData = false;

                try
                {
                    if (HarmonyDataLayer.ExistsDatafile(Name + "UI"))
                    {
                        UiDataFile stored = HarmonyDataLayer.ReadObject<UiDataFile>(Name + "UI");

                        if (stored?.Version == UI_DATA_VERSION && stored.Players != null)
                        {
                            UiLayoutVersions versions = stored.LayoutVersions;
                            bool legacyStageOne = versions == null;

                            bool migratedLayouts = false;

                            foreach (var pair in stored.Players)
                            {
                                UiPlayerData value = pair.Value;
                                Dictionary<UiType, UiOffsets> collection = null;

                                TryLoadOffset(ref collection, UiType.Buyable, value?.Buyable, versions?.Buyable ?? 0, BUYABLE_LAYOUT_VERSION);
                                TryLoadOffset(ref collection, UiType.Cooldown, value?.Cooldown, versions?.Cooldown ?? 0, COOLDOWN_LAYOUT_VERSION);
                                migratedLayouts |= TryLoadResizedOffset(ref collection, UiType.Delay, value?.Delay, versions?.Delay ?? 0, DELAY_LAYOUT_VERSION, DefaultDelayOffsets, new(-34.488f, 87.056f), new(179.631f, 124.804f), DelayUiWidth, DelayUiHeight);
                                TryLoadOffset(ref collection, UiType.Lockout, value?.Lockout, versions?.Lockout ?? 0, LOCKOUT_LAYOUT_VERSION);
                                TryLoadOffset(ref collection, UiType.PasteProgress, value?.PasteProgress, legacyStageOne ? PASTE_LAYOUT_VERSION : versions.PasteProgress, PASTE_LAYOUT_VERSION);
                                migratedLayouts |= TryLoadResizedOffset(ref collection, UiType.Status, value?.Status, legacyStageOne ? STATUS_LAYOUT_VERSION - 1 : versions.Status, STATUS_LAYOUT_VERSION, DefaultStatusOffsets, new(191.957f, 17.056f), new(327.626f, 79.024f), StatusUiMinimumWidth, StatusUiMinimumHeight);
                                TryLoadOffset(ref collection, UiType.Teleport, value?.Teleport, versions?.Teleport ?? 0, TELEPORT_LAYOUT_VERSION);

                                if (collection != null)
                                {
                                    Offsets[pair.Key] = collection;
                                }
                            }

                            if (migratedLayouts)
                            {
                                Instance.LogUiMessage("Migrated saved Status and PVP Delay positions for the rewritten UI while preserving their player-selected locations.");
                            }

                            rewriteData = legacyStageOne || !versions.IsCurrent || migratedLayouts;
                        }
                        else
                        {
                            rewriteData = true;
                            Instance.LogUiMessage("Reset legacy RaidableBasesUI offsets for the rewritten UI system.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    rewriteData = true;
                    Instance.QueueExceptionMessage($"Failed to read {Name}UI data; invalid offsets were reset: {ex}");
                }

                if (rewriteData)
                {
                    SaveOffsetData();
                }
            }

            private static void TryLoadOffset(ref Dictionary<UiType, UiOffsets> collection, UiType type, UiOffsets offsets, int storedVersion, int currentVersion)
            {
                if (storedVersion == currentVersion && IsValidOffset(offsets))
                {
                    (collection ??= new())[type] = offsets.Clone();
                }
            }

            private static bool TryLoadResizedOffset(ref Dictionary<UiType, UiOffsets> collection, UiType type, UiOffsets offsets, int storedVersion, int currentVersion, UiOffsets defaults, Vector2 legacyMin, Vector2 legacyMax, float minimumWidth, float minimumHeight)
            {
                bool current = storedVersion == currentVersion;
                bool previous = storedVersion == currentVersion - 1;

                if (!current && !previous)
                {
                    return false;
                }

                if (!IsValidOffset(offsets))
                {
                    return offsets != null;
                }

                UiOffsets migrated = offsets.Clone();
                Vector2 min = migrated.Min;
                Vector2 max = migrated.Max;
                bool changed = previous
                    ? MigrateUiRectangle(ref min, ref max, legacyMin, legacyMax, defaults.Min, defaults.Max, minimumWidth, minimumHeight)
                    : EnsureUiRectangle(ref min, ref max, defaults.Min, defaults.Max, minimumWidth, minimumHeight);
                migrated.Min = min;
                migrated.Max = max;
                (collection ??= new())[type] = migrated;
                return previous || changed;
            }

            private static bool IsValidOffset(UiOffsets offsets)
            {
                return offsets != null && offsets.Min.x < offsets.Max.x && offsets.Min.y < offsets.Max.y && IsFinite(offsets.Min.x) && IsFinite(offsets.Min.y) && IsFinite(offsets.Max.x) && IsFinite(offsets.Max.y) && IsFinite(offsets.NormalizedAnchor.x) && IsFinite(offsets.NormalizedAnchor.y);
            }

            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }

            private string mx(string key, string id = null, params object[] args)
            {
                return Instance.mx(key, id, args);
            }

            private void Notify(BasePlayer player, string key, params object[] args) => Instance.SendNotification(player, key, args);
        }

        #endregion UI

    }
}
