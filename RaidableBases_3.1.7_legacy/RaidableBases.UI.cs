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

        #region UI

        public enum UiType { Buyable, Cooldown, Delay, Lockout, Status, Teleport, Invalid }

        public UiHandler UI = new();

        public class Vector2Converter : JsonConverter
        {
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    var values = reader.Value.ToString().Trim().Split(' ');
                    return new Vector2(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]));
                }
                var o = Newtonsoft.Json.Linq.JObject.Load(reader);
                return new Vector2(Convert.ToSingle(o["x"]), Convert.ToSingle(o["y"]));
            }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var vector = (Vector2)value;
                writer.WriteValue($"{vector.x} {vector.y}");
            }
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector3);
            }
        }

        public class UiOffsets
        {
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 Min { get; set; }
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 Max { get; set; }
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
                return new(Min, Max);
            }
            public bool Equals(UiOffsets other)
            {
                return other != null && other.Min == Min && other.Max == Max;
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
            internal string MinString => $"{Left} {Top}";
            internal string MaxString => $"{Right} {Bottom}";
        }

        public class UiHandler
        {
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

            public static void AddCuiPanel(CuiElementContainer container, string color, string amin, string amax, string omin, string omax, string parent, string name, bool cursor = false, bool draggable = false)
            {
                var panel = new CuiPanel
                {
                    CursorEnabled = cursor,
                    Image = { Color = color },
                    RectTransform = { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
                };

                if (!draggable)
                {
                    container.Add(panel, parent, name, name);
                    return;
                }

                var host = new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    DestroyUi = name,
                    FadeOut = panel.FadeOut
                };

                if (panel.Image != null)
                    host.Components.Add(panel.Image);

                if (panel.RawImage != null)
                    host.Components.Add(panel.RawImage);

                if (panel.RectTransform != null)
                    host.Components.Add(panel.RectTransform);

                if (panel.CursorEnabled)
                    host.Components.Add(new CuiNeedsCursorComponent());

                if (panel.KeyboardEnabled)
                    host.Components.Add(new CuiNeedsKeyboardComponent());

                host.Components.Add(new CuiDraggableComponent
                {
                    LimitToParent = false,
                    MaxDistance = -1f,
                    AllowSwapping = false,
                    DropAnywhere = true,
                    DragAlpha = 0.98f,
                    ParentLimitIndex = 1,
                    Filter = "",
                    ParentPadding = "0 0",
                    AnchorOffset = "0 0",
                    KeepOnTop = false,
                    PositionRPC = CommunityEntity.DraggablePositionSendType.Relative,
                });

                container.Add(host);
            }

            public static void AddCuiButton(CuiElementContainer container, string buttonColor, string command, string text, string textColor, int fontSize, TextAnchor align, string amin, string amax, string omin, string omax, string parent, string name, string font = "robotocondensed-regular.ttf")
            {
                container.Add(new CuiButton
                {
                    Button = { Color = buttonColor, Command = command },
                    Text = { Text = text, Font = font, FontSize = fontSize, Align = align, Color = textColor },
                    RectTransform = { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
                }, parent, name, name);
            }

            public static void AddCuiElement(CuiElementContainer container, string text, int fontSize, TextAnchor align, string textColor, string amin, string amax, string omin, string omax, string parent, string name, string font = "robotocondensed-bold.ttf", string distance = "1 -1")
            {
                container.Add(new CuiElement
                {
                    DestroyUi = name,
                    Name = name,
                    Parent = parent,
                    Components = {
                    new CuiTextComponent { Text = text, Font = font, FontSize = fontSize, Align = align, Color = textColor },
                    new CuiOutlineComponent { Color = "0 0 0 0", Distance = distance },
                    new CuiRectTransformComponent { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
                }
                });
            }

            public static double ParseHexComponent(string hex, int j, int k) => hex.Length >= 6 && int.TryParse(hex.TrimStart('#').AsSpan(j, k), NumberStyles.AllowHexSpecifier, NumberFormatInfo.CurrentInfo, out var num) ? num : 1;

            public static string GetContrastColor(string hex) => ((ParseHexComponent(hex, 0, 2) * 299) + (ParseHexComponent(hex, 2, 2) * 587) + (ParseHexComponent(hex, 4, 2) * 114)) / 1000 >= 128 ? "0 0 0 1" : "1 1 1 1";

            public static string ConvertHexToRGBA(string hex, float a) => $"{ParseHexComponent(hex, 0, 2) / 255} {ParseHexComponent(hex, 2, 2) / 255} {ParseHexComponent(hex, 4, 2) / 255} {Mathf.Clamp(a, 0f, 1f)}";

            public static void DestroyUi(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, "RB_UI_Buyable");
                CuiHelper.DestroyUi(player, "RB_UI_Cooldown");
                CuiHelper.DestroyUi(player, "RB_UI_Delay");
                CuiHelper.DestroyUi(player, "RB_UI_Lockout");
                CuiHelper.DestroyUi(player, "RB_UI_Status");
                CuiHelper.DestroyUi(player, "RB_UI_Teleport");
            }

            public bool DestroyUi(BasePlayer player, UiType type)
            {
                if (config == null || !player.IsOnline() || !users.TryGetValue(player.userID, out var ui))
                {
                    return false;
                }

                TrySetMoveUi(player, type, true);

                switch (type)
                {
                    case UiType.Cooldown: CuiHelper.DestroyUi(player, "RB_UI_Cooldown"); ui.Cooldown?.Destroy(); break;
                    case UiType.Delay: CuiHelper.DestroyUi(player, "RB_UI_Delay"); ui.Delay?.Destroy(); break;
                    case UiType.Lockout: CuiHelper.DestroyUi(player, "RB_UI_Lockout"); ui.Lockout?.Destroy(); break;
                    case UiType.Status: CuiHelper.DestroyUi(player, "RB_UI_Status"); ui.Status?.Destroy(); break;
                    case UiType.Teleport: CuiHelper.DestroyUi(player, "RB_UI_Teleport"); ui.Teleport?.Destroy(); Teleport.Remove(player.userID); break;
                }

                if (ui.IsDestroyed)
                {
                    users.Remove(player.userID);
                    Movers.Remove(player.userID);
                }

                return true;
            }

            public void UpdateUi(BasePlayer player, UiType type)
            {
                if (config == null || !player.IsOnline())
                {
                    return;
                }

                var ui = TryAddUser(player);
                var isMovingUi = IsMovingUi(player, type);

                switch (type)
                {
                    case UiType.Buyable:
                        {
                            if (config.UI.Buyable.Enabled)
                            {
                                DestroyUi(player, UiType.Buyable);
                                ShowBuyableUi(player, isMovingUi);
                            }
                            break;
                        }
                    case UiType.Cooldown:
                        {
                            if (config.UI.BuyableCooldowns.Enabled)
                            {
                                DestroyUi(player, UiType.Cooldown);
                                if (ShowBuyableCooldownsUi(player, isMovingUi))
                                {
                                    ui.Cooldown?.Destroy();
                                    if (!isMovingUi) ui.Cooldown = Instance.timer.Once(60f, () => UpdateUi(player, UiType.Cooldown));
                                }
                                else PrivateEvents.Remove(player.userID);
                            }
                            break;
                        }
                    case UiType.Delay:
                        {
                            if (config.UI.Delay.Enabled && ShowDelayUi(player, isMovingUi))
                            {
                                ui.Delay?.Destroy();
                                if (!isMovingUi) ui.Delay = Instance.timer.Once(1f, () => UpdateUi(player, UiType.Delay));
                            }
                            break;
                        }
                    case UiType.Lockout:
                        {
                            if (config.UI.Lockout.Enabled)
                            {
                                DestroyUi(player, UiType.Lockout);
                                if (ShowLockoutsUi(player, isMovingUi))
                                {
                                    ui.Lockout?.Destroy();
                                    if (!isMovingUi) ui.Lockout = Instance.timer.Once(60f, () => UpdateUi(player, UiType.Lockout));
                                }
                                else PublicEvents.Remove(player.userID);
                            }
                            break;
                        }
                    case UiType.Status:
                        {
                            if (config.UI.Status.Enabled)
                            {
                                if (!ShowStatusUi(player, isMovingUi))
                                {
                                    return;
                                }
                                ui.Status?.Destroy();
                                if (!isMovingUi) ui.Status = Instance.timer.Once(1f, () => UpdateUi(player, UiType.Status));
                            }
                            break;
                        }
                    case UiType.Teleport:
                        {
                            if (ShowBuyableTeleportUi(player, isMovingUi))
                            {
                                ui.Teleport?.Destroy();
                                if (!isMovingUi) ui.Teleport = Instance.timer.Once(1f, () => UpdateUi(player, UiType.Teleport));
                            }
                            break;
                        }
                }
            }

            public void DestroyAll()
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    DestroyUi(player);
                }
            }

            private string GetPurchasePrice(string mode, string userid, out double price)
            {
                price = 0;

                if (!Instance.CanSpawnDifficultyToday(RaidableType.Purchased, mode))
                {
                    return null;
                }

                if (config.Settings.Buyable.Limits.Get(mode) < 0)
                {
                    return null;
                }

                using var prices = DisposableList<string>();
                var text = rf(mx($"Mode{mode}", userid));

                if (config.Settings.Management.TitleCase == true)
                {
                    text = text.TitleCase();
                }

                if (config.Settings.Include.Custom && config.Settings.Custom.TryGetValue(mode, out var o) && !o.IsNullOrEmpty())
                {
                    foreach (var cc in o)
                    {
                        if (cc.isItem)
                        {
                            prices.Add(mx("CustomDepositFormat", userid, cc.Amount, string.IsNullOrWhiteSpace(cc.Name) ? cc.Shortname : cc.Name));
                        }
                        if (cc.isPlugin)
                        {
                            prices.Add(mx("CustomDepositFormat", userid, cc.Plugin.Amount, cc.GetCurrencyName()));
                        }
                    }
                }

                if (config.Settings.Include.ServerRewards)
                {
                    price = config.Settings.ServerRewards.Get(mode);
                    if (price > 0)
                    {
                        prices.Add(mx("RP", userid, (int)price));
                    }
                }

                if (config.Settings.Include.Economics)
                {
                    price = config.Settings.Economics.Get(mode);
                    if (price > 0)
                    {
                        prices.Add(mx("$", userid, price));
                    }
                }

                return prices.Count == 0 ? null : mx("PriceText", userid, text, string.Join(", ", prices));
            }

            public float EstimateTextWidth(string text, float fontSize, float widthAdjustmentPercentage = 5f)
            {
                float baseWidth = text.Length * (fontSize * 0.475f);
                return baseWidth * (1 + (widthAdjustmentPercentage / 100f));
            }

            public float GetAdjustedTextWidth(string text, float fontSize, float maxWidth, float widthAdjustmentPercentage = 5f)
            {
                if (EstimateTextWidth(text, fontSize, widthAdjustmentPercentage) <= maxWidth)
                {
                    return EstimateTextWidth(text, fontSize, widthAdjustmentPercentage);
                }

                float ellipsisWidth = EstimateTextWidth("...", fontSize, widthAdjustmentPercentage);
                float availableWidth = maxWidth - ellipsisWidth;

                if (availableWidth <= 0)
                {
                    return ellipsisWidth;
                }

                int maxChars = (int)(availableWidth / (fontSize * 0.475f * (1 + (widthAdjustmentPercentage / 100f))));

                if (maxChars <= 0)
                {
                    return ellipsisWidth;
                }

                return EstimateTextWidth(text[..Math.Min(maxChars, text.Length)] + "...", fontSize, widthAdjustmentPercentage);
            }

            public void ShowBuyableUi(BasePlayer player, bool moveUI)
            {
                var ui = config.UI.Buyable;

                if (!ui.Enabled)
                {
                    return;
                }

                if (ui.FontSize < 1)
                {
                    ui.FontSize = 8;
                }

                var maxTextWidth = 0f;
                var modes = Instance.GetRaidableModes();
                using var buttons = DisposableList<(string mode, string text, string command, double value)>();

                foreach (string mode in modes)
                {
                    var text = GetPurchasePrice(mode, player.UserIDString, out double price);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    maxTextWidth = Math.Max(maxTextWidth, GetAdjustedTextWidth(text, ui.FontSize, maxWidth: 1000f));

                    int level = Instance.GetLevelFromMode(mode);
                    buttons.Add((mode, text, $"ui_buyraid {mode.Replace(" ", "__")}", ui.Price || level == -1 ? price : level));
                }

                if (buttons.Count == 0)
                {
                    SendError(player, RaidableType.Purchased);
                    return;
                }

                buttons.Sort((x, y) => x.value.CompareTo(y.value));

                var container = new CuiElementContainer();
                var panelWidth = Mathf.Max(200f, maxTextWidth);
                var panelAlpha = ui.PanelAlpha ?? 1f;
                var panelColor = ConvertHexToRGBA(ui.PanelColor, panelAlpha);
                var titleColor = ConvertHexToRGBA(ui.TitlePanelColor, panelAlpha);
                var closeColor = ConvertHexToRGBA(ui.CloseColor, panelAlpha);
                var offsets = GetOffsets(player.userID, UiType.Buyable);
                var buyRaids = mx("Buy Raids", player.UserIDString);
                var dir = moveUI ? "↑" : "→";
                var titleBarHeight = 31f;
                var topPadding = 10f;
                var bottomPadding = 10f;
                var spaceBetweenButtons = 5f;
                var buttonHeight = ui.FontSize + 10f;
                var totalButtonsHeight = buttonHeight * buttons.Count;
                var totalSpacing = spaceBetweenButtons * (buttons.Count - 1);
                var requiredButtonsHeight = totalButtonsHeight + totalSpacing;
                var requiredPanelHeight = titleBarHeight + topPadding + bottomPadding + requiredButtonsHeight;

                AddCuiPanel(container, panelColor, "0.5 0.1", "0.5 0.1", $"{-panelWidth / 2 + offsets.Left} {0 + offsets.Top}", $"{panelWidth / 2 + offsets.Left} {requiredPanelHeight + offsets.Top}", BUYABLE_PARENT, "RB_UI_Buyable", true, moveUI);
                AddCuiPanel(container, panelColor, "0 1", "1 1", $"0 -{titleBarHeight}", "0 0", "RB_UI_Buyable", "BR_TITLE_PANEL");
                AddCuiPanel(container, titleColor, "0 0.5", "0 0.5", "10 -17", "150 7", "BR_TITLE_PANEL", "BR_MOVE_PANEL");
                AddCuiButton(container, panelColor, $"rb_ui_move {UiType.Buyable}", $"{buyRaids} {dir}", "1 1 1 1", ui.FontSize, TextAnchor.MiddleCenter, "0 0", "1 1", "2 2", "-2 -2", "BR_MOVE_PANEL", "BR_MOVE_BUTTON");
                AddCuiPanel(container, titleColor, "1 0.5", "1 0.5", "-50 -17", "-10 7", "BR_TITLE_PANEL", "BR_CLOSE_PANEL");
                AddCuiButton(container, panelColor, "ui_buyraid closeui", "ⓧ", closeColor, ui.FontSize, TextAnchor.MiddleCenter, "0 0", "1 1", "2 2", "-2 -2", "BR_CLOSE_PANEL", "BR_CLOSE_BUTTON");

                for (int i = 0; i < buttons.Count; i++)
                {
                    var (mode, text, command, price) = buttons[i];
                    var buttonY = (titleBarHeight + topPadding) + i * (buttonHeight + spaceBetweenButtons);
                    var buttonColor = ConvertHexToRGBA(ui.Difficulty ? config.Settings.Management.Colors2.Get(mode) : ui.GetButton(mode), ui.ButtonAlpha);
                    var buttonTextColor = ui.Contrast ? GetContrastColor(ui.Difficulty ? config.Settings.Management.Colors2.Get(mode) : ui.GetButton(mode)) : ConvertHexToRGBA(ui.GetText(mode), 1f);

                    AddCuiButton(container, buttonColor, command, text, buttonTextColor, ui.FontSize, TextAnchor.MiddleCenter, "0 1", "1 1", $"10 {-buttonY - buttonHeight}", $"-10 {-buttonY}", "RB_UI_Buyable", $"BR_{mode}_BUTTON");
                }

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

                CuiHelper.AddUi(player, container);

                if (moveUI)
                {
                    TrySetMoveUi(player, UiType.Buyable);
                }
            }

            public HashSet<ulong> PrivateEvents = new();
            public HashSet<ulong> PublicEvents = new();

            private void SendError(BasePlayer player, RaidableType type)
            {
                if (!config.Settings.Include.Any)
                {
                    Message(player, "NoBuyableEventsCostsEnabled");
                }

                if (!config.Settings.ServerRewards.Any() && !config.Settings.Economics.Any() && !config.Settings.AnyCustomCost())
                {
                    Message(player, "NoBuyableEventsCostsConfigured");
                }

                if (!Instance.AllowBuyingPVP && Instance.Buildings.Profiles.All(profile => profile.Value.Options.AllowPVP))
                {
                    Message(player, "NoBuyableEventsPVP");
                }

                if (!config.Settings.Management.Amounts.Any())
                {
                    Message(player, "NoBuyableEventsEnabled");
                }

                if (!Instance.GetRaidableModes().Exists(x => Instance.CanSpawnDifficultyToday(type, x)))
                {
                    Message(player, "NoBuyableEventsToday");
                }
            }

            public bool ShowBuyableTeleportUi(BasePlayer player, bool moveUI, float seconds = 0f, string mode = RaidableMode.Random)
            {
                if (!Teleport.TryGetValue(player.userID, out var ts))
                {
                    ulong userid = player.userID;
                    Teleport[userid] = ts = new()
                    {
                        Timer = Instance.timer.Once(seconds, () =>
                        {
                            DestroyUi(player, UiType.Teleport);
                            Teleport.Remove(userid);
                        }),
                        time = Time.time + seconds,
                        mode = mode
                    };

                    UpdateUi(player, UiType.Teleport);
                    return false;
                }

                if (ts.time < Time.time)
                {
                    DestroyUi(player, UiType.Teleport);
                    ts.Destroy();
                    return false;
                }

                if (IsMovingUi(player, UiType.Teleport))
                {
                    return true;
                }

                var container = new CuiElementContainer();
                var ui = config.UI.Buyable;
                var panelAlpha = ui.PanelAlpha ?? 1f;
                var panelColor = ConvertHexToRGBA(ui.PanelColor, panelAlpha);
                var closeColor = ConvertHexToRGBA(ui.CloseColor, panelAlpha);
                var acceptColor = ConvertHexToRGBA(ui.Difficulty ? config.Settings.Management.Colors2.Get(ts.mode) : ui.GetButton(ts.mode), ui.ButtonAlpha);
                var time = Mathf.CeilToInt(ts.time - Time.time);
                var lblTeleport = mx("Teleport Question", player.UserIDString);
                var lblTime = mx("Teleport Seconds To Accept", player.UserIDString, time);

                AddCuiPanel(container, panelColor, "0.5 0", "0.5 0", "-116.034 88.736", "116.045 205.264", TELEPORT_PARENT, "RB_UI_Teleport", moveUI, moveUI);
                AddCuiButton(container, panelColor, $"ui_buyraid accept_teleport", mx("Accept", player.UserIDString), acceptColor, ui.FontSize, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", "-93.95 -46.28", "-48.45 -29.12", "RB_UI_Teleport", "BT_ACCEPT_BUTTON");
                AddCuiButton(container, panelColor, $"ui_buyraid decline_teleport", mx("Decline", player.UserIDString), closeColor, ui.FontSize, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", "48.55 -46.28", "94.05 -29.12", "RB_UI_Teleport", "BR_DECLINE_BUTTON");
                AddCuiElement(container, lblTeleport, ui.FontSize, TextAnchor.MiddleCenter, acceptColor, "0.5 0.5", "0.5 0.5", "-116.04 21.341", "116.04 55.021", "RB_UI_Teleport", "ST_TELEPORT_LABEL");
                AddCuiElement(container, lblTime, ui.FontSize, TextAnchor.MiddleCenter, closeColor, "0.5 0.5", "0.5 0.5", "-116.039 -12.34", "116.041 21.34", "RB_UI_Teleport", "ST_TIME_LABEL");

                CuiHelper.AddUi(player, container);

                if (moveUI)
                {
                    TrySetMoveUi(player, UiType.Teleport);
                }

                return true;
            }

            public bool ShowDelayUi(BasePlayer player, bool moveUI)
            {
                if (player.IsKilled())
                {
                    return false;
                }

                if (!Instance.GetPVPDelay(player.userID, false, out DelaySettings ds))
                {
                    DestroyUi(player, UiType.Delay);
                    return false;
                }

                if (ds.time < Time.time)
                {
                    Instance.RemovePVPDelay(player.userID, ds);
                    DestroyUi(player, UiType.Delay);
                    ds.Destroy();
                    return false;
                }

                if (Instance.EventTerritory(player.transform.position))
                {
                    DestroyUi(player, UiType.Delay);
                    return true;
                }

                if (IsMovingUi(player, UiType.Delay))
                {
                    return true;
                }

                var ui = config.UI.Delay;
                var container = new CuiElementContainer();
                var panelAlpha = ui.PanelAlpha ?? 1f;
                var panelColor = ConvertHexToRGBA(ui.PanelColor, panelAlpha);
                var tpColor = ConvertHexToRGBA(ui.TitlePanelColor, panelAlpha);
                var textcolor = ConvertHexToRGBA(ui.TextColor, panelAlpha);
                var time = mx("PVP Delay", player.UserIDString, Mathf.CeilToInt(ds.time - Time.time));
                var offsets = GetOffsets(player.userID, UiType.Delay);
                var dir = moveUI ? "↑" : "→";

                AddCuiPanel(container, panelColor, "0.5 0", "0.5 0", offsets.MinString, offsets.MaxString, DELAY_PARENT, "RB_UI_Delay", moveUI, moveUI);
                AddCuiPanel(container, tpColor, "0.5 0.5", "0.5 0.5", "-99.218 -12.813", "99.218 12.812", "RB_UI_Delay", "PD_TITLE_PANEL");
                AddCuiPanel(container, panelColor, "0.5 0.5", "0.5 0.5", "-91.795 -10.485", "91.495 10.091", "PD_TITLE_PANEL", "PD_EMBED_PANEL");
                AddCuiButton(container, panelColor, $"rb_ui_move {UiType.Delay}", time + " " + dir, textcolor, ui.FontSize, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", "-91.647 -10.288", "91.645 10.288", "PD_EMBED_PANEL", "PD_EMBED_LABEL");

                CuiHelper.AddUi(player, container);

                if (moveUI)
                {
                    TrySetMoveUi(player, UiType.Delay);
                }

                return true;
            }

            public float GetBoundSize(BaseEntity ent) => ent == null ? 0f : ent.bounds.size.Max();

            public bool ShowStatusUi(BasePlayer player, bool moveUI)
            {
                float parentRadius = player.HasParent() ? GetBoundSize(player.GetParentEntity()) : 0f;
                float mountRadius = player.isMounted ? GetBoundSize(player.GetMounted()) : 0f;
                float radius = 5f + Mathf.Max(parentRadius, mountRadius);

                if (!Instance.Get(player.transform.position, out var raid, radius) || raid.IsDespawning)
                {
                    DestroyUi(player, UiType.Status);
                    return false;
                }

                if (IsMovingUi(player, UiType.Status))
                {
                    return true;
                }

                var ui = config.UI.Status;
                var container = new CuiElementContainer();
                var panelAlpha = ui.PanelAlpha ?? 1f;
                var colorPanel = ConvertHexToRGBA(ui.PanelColor, panelAlpha);
                var colorTitle = ConvertHexToRGBA(ui.TitlePanelColor, panelAlpha);
                var colorAllow = ConvertHexToRGBA(raid.AllowPVP ? ui.ColorPVP : ui.ColorPVE, 1f);
                var textAllow = raid.AllowPVP ? mx(raid.Options.Eco.Enabled ? "PVP ECO UI" : "PVP UI", player.UserIDString) : mx(raid.Options.Eco.Enabled ? "PVE ECO UI" : "PVE UI", player.UserIDString);
                var offsets = GetOffsets(player.userID, UiType.Status);
                var dir = moveUI ? "↑" : "→";

                SetOwner(raid, ui, player, out var ownerName, out var ownerColor);

                if (offsets.NormalizedAnchor != Vector2.zero)
                {
                    Vector2 anchor = offsets.NormalizedAnchor;
                    float width = offsets.Max.x - offsets.Min.x;
                    float height = offsets.Max.y - offsets.Min.y;
                    AddCuiPanel(container, colorPanel, Vec2ToString(anchor), Vec2ToString(anchor), $"{-width * 0.5f:0.###} {-height * 0.5f:0.###}", $"{width * 0.5f:0.###} {height * 0.5f:0.###}", STATUS_PARENT, "RB_UI_Status", moveUI, moveUI);
                }
                else
                {
                    AddCuiPanel(container, colorPanel, "0.5 0", "0.5 0", offsets.MinString, offsets.MaxString, STATUS_PARENT, "RB_UI_Status", moveUI, moveUI);
                }

                AddCuiPanel(container, colorTitle, "0.5 0.5", "0.5 0.5", "-58.355 18.811", "58.265 44.436", "RB_UI_Status", "ST_TITLE_PANEL");
                AddCuiPanel(container, colorPanel, "0.5 0.5", "0.5 0.5", "-53.432 -10.288", "53.432 10.288", "ST_TITLE_PANEL", "ST_PVP_PANEL");
                if (raid.DespawnTime > 0)
                {
                    AddCuiButton(container, colorPanel, $"rb_ui_move {UiType.Status}", mx("UIFormatLockoutMinutes", player.UserIDString, raid.DespawnTime), "1 1 1 1", ui.FontSize, TextAnchor.MiddleCenter, "0 0", "1 1", "55.871 0.697", "0.333 0", "ST_PVP_PANEL", "ST_DESPAWN_LABEL");
                }
                AddCuiButton(container, colorPanel, $"rb_ui_move {UiType.Status}", textAllow + " " + dir, colorAllow, ui.FontSize, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", "-53.429 -10.288", "2.439 10.288", "ST_PVP_PANEL", "ST_PVP_LABEL");
                AddCuiElement(container, raid.IsOpened ? mx("Owner", player.UserIDString) : ownerName, ui.FontSize, TextAnchor.MiddleLeft, "1 0.87 0.05 1", "0 0", "1 1", "9.853 2.775", "-85.197 -38.414", "RB_UI_Status", "ST_OWNER_LABEL");
                AddCuiElement(container, raid.IsOpened ? ownerName : mx("Completed", player.UserIDString), ui.FontSize, TextAnchor.MiddleRight, ownerColor, "0 0", "1 1", "50.473 2.774", "-9.194 -38.415", "RB_UI_Status", "ST_NAME_LABEL");
                if (ui.ShowLootLeft)
                {
                    if (!ui.HideWithoutOwner || raid.ownerId.IsSteamId())
                    {
                        AddCuiElement(container, mx("Loot", player.UserIDString), ui.FontSize, TextAnchor.MiddleLeft, "1 0.87 0.05 1", "0 0", "1 1", "9.851 23.555", "-69.078 -17.644", "RB_UI_Status", "ST_LOOT_LABEL");
                        AddCuiElement(container, raid.GetLootAmountRemaining().ToString(), ui.FontSize, TextAnchor.MiddleRight, "1 1 1 1", "0 0", "1 1", "66.595 23.555", "-9.195 -17.644", "RB_UI_Status", "ST_LOOTLEFT_LABEL");
                    }
                }

                CuiHelper.AddUi(player, container);

                if (moveUI)
                {
                    TrySetMoveUi(player, UiType.Status);
                }

                return true;
            }

            private static string Vec2ToString(Vector2 v) => $"{v.x:0.###} {v.y:0.###}";

            private void SetOwner(RaidableBase raid, UIStatusSettings ui, BasePlayer player, out string ownerName, out string ownerColor)
            {
                ownerColor = ui.NoneColor;
                ownerName = mx("None", player.UserIDString);

                if (raid.ownerId.IsSteamId())
                {
                    if (raid.ownerId == player.userID)
                    {
                        ownerColor = ui.PositiveColor;
                        ownerName = mx("You", player.UserIDString);
                    }
                    else if (raid.GetRaider(player).IsAlly || raid.IsAlly(raid.ownerId, player.userID))
                    {
                        ownerColor = ui.PositiveColor;
                        ownerName = mx("Ally", player.UserIDString);
                    }
                    else
                    {
                        ownerColor = ui.NegativeColor;
                        ownerName = mx("Enemy", player.UserIDString);
                    }
                }

                if (config.Settings.Management.LockTime > 0f)
                {
                    float time = raid.GetRaider(player).lastActiveTime;
                    float secondsLeft = Mathf.Max(0f, (config.Settings.Management.LockTime * 60f) - (Time.time - time));
                    ownerName = $"{ownerName} ({mx("UiInactiveTimeLeft", player.UserIDString, GetMinutes(secondsLeft).ToString())})";
                }

                ownerColor = ConvertHexToRGBA(ownerColor, 1f);
            }

            public void CreateUi(BasePlayer player, bool moveUI, UiType type, string name, float alpha, string title, string titleColor, string backgroundColor, string titlePanelColor, string titleEmbedColor, UiOffsets offsets, List<(string mode, string text)> modes)
            {
                var container = new CuiElementContainer();
                var rcSpacing = new Vector2(32.4f, -19.455f);
                var initialPanel = new Vector4(-47.717f, 1.546f, -17.283f, 19.454f);
                var totalRows = (int)Math.Ceiling(modes.Count / 3.0);
                var additionalHeight = (totalRows - 1) * Math.Abs(rcSpacing.y);
                var parentMinY = offsets.Top - additionalHeight;
                var parentMin = $"{offsets.Left} {parentMinY}";
                var parentMax = $"{offsets.Right} {offsets.Bottom}";
                var extraRows = totalRows - 3;
                var yCorrection = extraRows * Math.Abs(rcSpacing.y) / 2;

                AddCuiPanel(container, backgroundColor, "1 0.5", "1 0.5", parentMin, parentMax, type == UiType.Lockout ? LOCKOUT_PARENT : COOLDOWN_PARENT, name, moveUI, moveUI);
                AddCuiPanel(container, titlePanelColor, "0.5 0.5", "0.5 0.5", $"-50.065 {24.488 + yCorrection}", $"50.065 {45.711 + yCorrection}", name, $"{name}_PANEL");
                AddCuiButton(container, titleEmbedColor, $"rb_ui_move {type}", $"{title} {(moveUI ? "↑" : "→")}", titleColor, 8, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", "-46.192 -8.12", "46.194 8.12", $"{name}_PANEL", $"{name}_DESC_BUTTON");

                for (int i = 0; i < modes.Count; i++)
                {
                    var (mode, text) = modes[i];
                    var row = i / 3;
                    var column = i % 3;
                    var panel = new Vector4(initialPanel.x + (rcSpacing.x * column), initialPanel.y + (rcSpacing.y * row) + yCorrection, initialPanel.z + (rcSpacing.x * column), initialPanel.w + (rcSpacing.y * row) + yCorrection);

                    AddCuiPanel(container, GetBackgroundColor(mode, alpha), "0.5 0.5", "0.5 0.5", $"{panel.x} {panel.y}", $"{panel.z} {panel.w}", name, $"{name}_{mode}_PANEL");
                    AddCuiElement(container, text, 8, TextAnchor.MiddleCenter, GetTextColor(mode), "0 0", "1 1", "0 0", "0 0", $"{name}_{mode}_PANEL", $"{name}_{mode}_LABEL");
                }

                CuiHelper.AddUi(player, container);

                if (moveUI)
                {
                    TrySetMoveUi(player, type);
                }
            }

            public bool ShowLockoutsUi(BasePlayer player, bool moveUI)
            {
                if (!data.Lockouts.TryGetValue(player.UserIDString, out var lo))
                {
                    TrySetMoveUi(player, UiType.Lockout, true);
                    return false;
                }

                if (!lo.Any())
                {
                    CuiHelper.DestroyUi(player, "RB_UI_Lockout");
                    TrySetMoveUi(player, UiType.Lockout, true);
                    return false;
                }

                if (IsMovingUi(player, UiType.Lockout))
                {
                    return true;
                }

                var ui = Instance.config.UI.Lockout;

                if (ui.BuyOnly && !PublicEvents.Contains(player.userID))
                {
                    return false;
                }

                List<(string, string)> mins = new();

                foreach (var mode in Instance.GetRaidableModes())
                {
                    if (lo.Get(mode) <= 0) continue;
                    mins.Add((mode, mx("UIFormatLockoutMinutes", player.UserIDString, GetMinutes(mode, lo))));
                }

                var title = mx("Normal Lockouts", player.UserIDString);
                var offsets = GetOffsets(player.userID, UiType.Lockout);

                CreateUi(player, moveUI, UiType.Lockout, "RB_UI_Lockout", ui.Alpha, title, ConvertHexToRGBA(ui.TitleColor, ui.Alpha), ConvertHexToRGBA(ui.BackgroundColor, ui.Alpha), ConvertHexToRGBA(ui.TitlePanelColor, ui.Alpha), ConvertHexToRGBA(ui.TitleEmbedColor, ui.Alpha), offsets, mins);

                return true;
            }

            public bool ShowBuyableCooldownsUi(BasePlayer player, bool moveUI)
            {
                if (Instance.RaidableModes.Count == 0 && Instance.IsGridLoading())
                {
                    TrySetMoveUi(player, UiType.Cooldown, true);
                    Message(player, "GridIsLoading");
                    return false;
                }

                data.BuyableCooldowns.RemoveAll((userid, bin) => userid.HasPermission("raidablebases.buyable.bypass.cooldown") || !BuyableInfo.HasTimeRemaining(Instance, userid));

                if (!data.BuyableCooldowns.ContainsKey(player.userID))
                {
                    TrySetMoveUi(player, UiType.Cooldown, true);
                    return false;
                }

                var ui = Instance.config.UI.BuyableCooldowns;

                if (ui.BuyOnly && !PrivateEvents.Contains(player.userID))
                {
                    return false;
                }

                if (IsMovingUi(player, UiType.Cooldown))
                {
                    return true;
                }

                var mins = new List<(string, string)>();
                var title = mx("Buyable Cooldowns", player.UserIDString);
                var offsets = GetOffsets(player.userID, UiType.Cooldown);

                foreach (var mode in Instance.GetRaidableModes())
                {
                    var minutes = GetMinutes(player, mode);
                    if (minutes <= 0) continue;
                    mins.Add((mode, mx("UIFormatLockoutMinutes", player.UserIDString, minutes)));
                }

                CreateUi(player, moveUI, UiType.Cooldown, "RB_UI_Cooldown", ui.Alpha, title, ConvertHexToRGBA(ui.TitleColor, ui.Alpha), ConvertHexToRGBA(ui.BackgroundColor, ui.Alpha), ConvertHexToRGBA(ui.TitlePanelColor, ui.Alpha), ConvertHexToRGBA(ui.TitleEmbedColor, ui.Alpha), offsets, mins);

                return true;
            }

            public void TrySetMoveUi(BasePlayer player, UiType type, bool destroyingUi = false)
            {
                ulong userid = player.userID;
                if (destroyingUi)
                {
                    DestroyTimer(player, player.userID, type, false);
                    return;
                }
                if (!Movers.TryGetValue(userid, out var types))
                {
                    Movers[userid] = types = new();
                }
                if (types.TryGetValue(type, out var closer) && closer is { Destroyed: false })
                {
                    closer.Reset();
                }
                else types[type] = Instance.timer.Once(10f, () =>
                {
                    DestroyTimer(player, userid, type, true);
                });
            }

            public void DestroyTimer(BasePlayer player, ulong userid, UiType type, bool update = false)
            {
                if (Movers.TryGetValue(userid, out var types) && types.Remove(type, out var closer))
                {
                    if (types.Count == 0)
                    {
                        Movers.Remove(userid);
                        if (Instance.SaveOffsetDataTimer is { Destroyed: false }) Instance.SaveOffsetDataTimer.Reset();
                    }
                    if (closer != null && !closer.Destroyed)
                    {
                        closer.Destroy();
                    }
                    if (update && player != null)
                    {
                        UpdateUi(player, type);
                    }
                }
            }

            public bool IsMovingUi(BasePlayer player, UiType type) => Movers.TryGetValue(player.userID, out var types) && types.ContainsKey(type);

            private double GetMinutes(double value) => Math.Ceiling(TimeSpan.FromSeconds(value).TotalMinutes);

            private double GetMinutes(BasePlayer buyer, string mode) => GetMinutes(Math.Max(0, BuyableInfo.GetTimeRemaining(Instance, buyer, mode, false)));

            private string GetMinutes(string mode, Lockout lo) => GetMinutes(Math.Max(0, lo.Get(mode))).ToString();

            private string GetBackgroundColor(string mode, float alpha) => ConvertHexToRGBA(config.UI.Buyable.Difficulty ? config.Settings.Management.Colors2.Get(mode) : config.UI.Buyable.GetButton(mode), alpha);

            private string GetTextColor(string mode) => config.UI.Buyable.Contrast ? GetContrastColor(config.UI.Buyable.Difficulty ? config.Settings.Management.Colors2.Get(mode) : config.UI.Buyable.GetButton(mode)) : ConvertHexToRGBA(config.UI.Buyable.GetText(mode), 1f);

            public PlayerUi TryAddUser(BasePlayer player)
            {
                if (!Offsets.TryGetValue(player.userID, out var ui) || ui == null)
                {
                    Offsets[player.userID] = ui = new();
                }
                TrySetDefaultTypes(ui);
                if (!users.TryGetValue(player.userID, out var pi) || pi == null)
                {
                    users[player.userID] = pi = new();
                }
                return pi;
            }

            public UiOffsets GetOffsets(ulong userid, UiType type)
            {
                if (!Offsets.TryGetValue(userid, out var ui) || ui == null)
                {
                    Offsets[userid] = ui = new();
                }
                return TrySetDefaultTypes(ui)[type];
            }

            private Dictionary<UiType, UiOffsets> TrySetDefaultTypes(Dictionary<UiType, UiOffsets> ui)
            {
                if (!ui.TryGetValue(UiType.Buyable, out var offsets) || !IsValidOffset(offsets))
                {
                    ui[UiType.Buyable] = DefaultBuyableOffsets.Clone();
                }
                if (!ui.TryGetValue(UiType.Cooldown, out offsets) || !IsValidOffset(offsets))
                {
                    ui[UiType.Cooldown] = DefaulCooldownOffsets.Clone();
                }
                if (!ui.TryGetValue(UiType.Delay, out offsets) || !IsValidOffset(offsets))
                {
                    ui[UiType.Delay] = DefaultDelayOffsets.Clone();
                }
                if (!ui.TryGetValue(UiType.Lockout, out offsets) || !IsValidOffset(offsets))
                {
                    ui[UiType.Lockout] = DefaultLockoutOffsets.Clone();
                }
                if (!ui.TryGetValue(UiType.Status, out offsets) || !IsValidOffset(offsets))
                {
                    ui[UiType.Status] = DefaultStatusOffsets.Clone();
                }
                return ui;
            }

            public void SaveOffsetData()
            {
                if (Offsets == null)
                {
                    return;
                }
                var obj = new Dictionary<ulong, Dictionary<UiType, UiOffsets>>();
                foreach (var (userid, offsets) in Offsets)
                {
                    var ui = new Dictionary<UiType, UiOffsets>();
                    foreach (var (type, offset) in offsets)
                    {
                        if (!IsDefault(type, offset))
                        {
                            ui[type] = offset;
                        }
                    }
                    if (ui.Count > 0)
                    {
                        obj[userid] = ui;
                    }
                }
                HarmonyDataLayer.WriteObject(Name + "UI", obj);
            }

            private bool IsDefault(UiType type, UiOffsets offsets) => type switch
            {
                UiType.Buyable => offsets.Equals(DefaultBuyableOffsets),
                UiType.Cooldown => offsets.Equals(DefaulCooldownOffsets),
                UiType.Delay => offsets.Equals(DefaultDelayOffsets),
                UiType.Lockout => offsets.Equals(DefaultLockoutOffsets),
                UiType.Status => offsets.Equals(DefaultStatusOffsets),
                _ => false,
            };

            public void LoadOffsetData()
            {
                try { Offsets = HarmonyDataLayer.ReadObject<Dictionary<ulong, Dictionary<UiType, UiOffsets>>>(Name + "UI"); } catch { }
                Offsets ??= new();
                DefaultBuyableOffsets = new(config.UI.Buyable.OffsetMin, config.UI.Buyable.OffsetMax);
                DefaulCooldownOffsets = new(config.UI.BuyableCooldowns.OffsetMin, config.UI.BuyableCooldowns.OffsetMax);
                DefaultDelayOffsets = new(config.UI.Delay.OffsetMin, config.UI.Delay.OffsetMax);
                DefaultLockoutOffsets = new(config.UI.Lockout.OffsetMin, config.UI.Lockout.OffsetMax);
                DefaultStatusOffsets = new(config.UI.Status.OffsetMin, config.UI.Status.OffsetMax);
            }

            private bool IsValidOffset(UiOffsets offsets)
            {
                if (offsets == null) return false;
                return offsets.Min != default || offsets.Max != default;
            }

            private void Message(BasePlayer player, string key, params object[] args) => Instance.Message(player, key, args);

            private string mx(string key, string id = null, params object[] args) => Instance.mx(key, id, args);

            public UiOffsets DefaultBuyableOffsets, DefaulCooldownOffsets, DefaultDelayOffsets, DefaultLockoutOffsets, DefaultStatusOffsets;

            public Dictionary<ulong, Dictionary<UiType, UiOffsets>> Offsets;
            public Dictionary<ulong, Dictionary<UiType, Timer>> Movers = new();
            public Dictionary<ulong, TimeSettings> Teleport = new();

            public const double SPACING_Y = 28.5;

            public Dictionary<ulong, PlayerUi> users = new();

            public class PlayerUi
            {
                public BasePlayer player;
                public Timer Cooldown, Delay, Lockout, Status, Teleport;
                public bool IsDestroyed => Cooldown == null & Delay == null & Lockout == null && Status == null && Teleport == null;
            }
        }

        #endregion UI

    }
}
