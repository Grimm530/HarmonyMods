using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Facepunch;
using Network;
using UnityEngine;
using Oxide.Ext.Chaos.UIFramework;
using Color = Oxide.Ext.Chaos.UIFramework.Color;
using Font = Oxide.Ext.Chaos.UIFramework.Font;
using GridLayoutGroup = Oxide.Ext.Chaos.UIFramework.GridLayoutGroup;
using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;

namespace TeleportGUI
{
    public partial class TeleportGUIMod
    {
        public class WarpForm { public string Name; public string Permission; public string Command; }
        private const string TPUI = "teleport.ui";
        private const string TPR_POPUP = "teleportrequest.ui.popup";
        private const string TPP_POPUP = "teleportpending.ui.popup";

        private string m_MagnifyImage;

        /*private Style m_BackgroundStyle;
        private Style m_PanelStyle;
        private Style m_HeaderStyle;
        private Style m_ButtonStyle;
        private Style m_ButtonDisabledStyle;
        private Style m_CloseStyle;
        private Style m_ToggleStyle;*/

        //private string m_Cd = "cd.8471";

        private StylePreset m_StylePreset;
        
        private OutlineComponent m_OutlineClose;
        private OutlineComponent m_OutlineHighlight;
        private OutlineComponent m_OutlineDark = new OutlineComponent(new Color(0.1647059f, 0.1803922f, 0.1921569f));

        private readonly GridLayoutGroup m_GridLayout = new GridLayoutGroup(2, 16, Axis.Horizontal)
        {
            Area = new Area(-220f, -215f, 220f, 215f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(5f, 5f, 5f, 5f),
            Corner = Corner.TopLeft,
        };
        
        private CommandCallbackHandler m_CallbackHandler;

        private void SetupUIComponents()
        {
            if (m_CallbackHandler == null)
                m_CallbackHandler = new CommandCallbackHandler(this);

            var colors = _config?.UI?.Colors;
            if (colors == null) return;

            m_StylePreset = new StylePreset
            {
                Background = new Style(ChaosStyle.Background)
                {
                    ImageColor = new Color(colors.Background.Hex, colors.Background.Alpha)
                },
                Panel = new Style(ChaosStyle.Panel)
                {
                    ImageColor = new Color(colors.Panel.Hex, colors.Panel.Alpha)
                },
                Header = new Style(ChaosStyle.Header)
                {
                    ImageColor = new Color(colors.Header.Hex, colors.Header.Alpha),
                    Sprite = Sprites.Background_Rounded_top,
                    FontSize = 14,
                    Font = Font.PermanentMarker,
                    Alignment = TextAnchor.MiddleLeft
                },
                Button = new Style(ChaosStyle.Button)
                {
                    ImageColor = new Color(colors.Button.Hex, colors.Button.Alpha)
                },
                DisabledButton = new Style(ChaosStyle.DisabledButton)
                {
                    ImageColor = new Color(colors.Button.Hex, Mathf.Min(colors.Button.Alpha, 0.8f)),
                    FontColor = new Color(1f, 1f, 1f, 0.2f),
                },
                Close = new Style(ChaosStyle.Close)
                {
                    FontSize = 16
                },
                Toggle = new Style(ChaosStyle.Toggle)
                {
                    ImageColor = new Color(colors.Highlight.Hex, colors.Highlight.Alpha)
                }
            };

            m_OutlineClose = new OutlineComponent(new Color(colors.Close.Hex, colors.Close.Alpha));
            m_OutlineHighlight = new OutlineComponent(new Color(colors.Highlight.Hex, colors.Highlight.Alpha));
            
            if (ImageLibrary.IsLoaded)
            {
                ImageLibrary.AddImage("https://chaoscode.io/oxide/Images/magnifyingglass.png", "teleportgui.search", 0UL, () =>
                {
                    m_MagnifyImage = ImageLibrary.GetImage("teleportgui.search");
                });
            }
        }
        
        #region Teleport
        private enum UiMode { Teleport, Home, Warp }

        private static UiMode ParseUiMode(string mode)
        {
            if (string.Equals(mode, "home", StringComparison.OrdinalIgnoreCase)) return UiMode.Home;
            if (string.Equals(mode, "warp", StringComparison.OrdinalIgnoreCase)) return UiMode.Warp;
            return UiMode.Teleport;
        }

        private static string UiModeToKey(UiMode mode) => mode switch
        {
            UiMode.Home => "home",
            UiMode.Warp => "warp",
            _ => "teleport"
        };

        private static TeleportPaymentKind PaymentKindFromUiMode(UiMode mode) => mode switch
        {
            UiMode.Home => TeleportPaymentKind.Home,
            UiMode.Warp => TeleportPaymentKind.Warp,
            _ => TeleportPaymentKind.Teleport
        };

        public void ShowTeleportUI(BasePlayer player, string mode)
        {
            ShowTeleportUI(player, ParseUiMode(mode));
        }

        private void ShowTeleportUI(BasePlayer player, UiMode mode)
        {
            TeleportGUIData.UserData userData = GetOrCreateUser(player);

            BaseContainer root = ChaosPrefab.Background(TPUI, Layer.Hud, UIAnchor.Center, new Offset(-225f, -265f, 225f, 265f), m_StylePreset)
                .WithChildren(parent =>
                {
                    CreateModeSelector(player, parent, mode);
                    
                    CreateTitleBar(player, userData, parent);

                    if (mode == UiMode.Teleport)
                    {
                        List<BasePlayer> list = BuildPlayerList(player, userData);
                        
                        CreateHeaderBar(player, userData, parent, m_GridLayout.HasNextPage(GetUiPage(player), list.Count), mode);
                        CreateGridLayout(player, userData, parent, list, mode, 
                            (layout, anchor, offset, t) => CreatePlayerEntry(player, userData, layout, anchor, offset, t));
                        
                        Pool.FreeUnmanaged(ref list);
                    }
                    
                    if (mode == UiMode.Home)
                    {
                        List<KeyValuePair<string, TeleportGUIData.UserData.HomePoint>> list = BuildHomeList(player, userData);
                        
                        CreateHeaderBar(player, userData, parent, m_GridLayout.HasNextPage(GetUiPage(player), list.Count), mode);
                        CreateGridLayout(player, userData, parent, list, mode, 
                            (layout, anchor, offset, t) => CreateHomeEntry(player, userData, layout, anchor, offset, t));
                        
                        Pool.FreeUnmanaged(ref list);
                    }
                    
                    if (mode == UiMode.Warp)
                    {
                        List<UiWarpRow> list = BuildWarpList(player);
                        
                        CreateHeaderBar(player, userData, parent, m_GridLayout.HasNextPage(GetUiPage(player), list.Count), mode);
                        CreateGridLayout(player, userData, parent, list, mode, 
                            (layout, anchor, offset, t) => CreateWarpEntry(player, userData, layout, anchor, offset, new KeyValuePair<string, TeleportGUIData.WarpPoint>(t.Name, t.Point), t.IsManual));
                        
                        Pool.FreeUnmanaged(ref list);
                    }
                })
                .NeedsCursor()
                .NeedsKeyboard()
                .DestroyExisting();

            ChaosUI.Show(player, root);
        }

        private void CreateTitleBar(BasePlayer player, TeleportGUIData.UserData userData, BaseContainer parent)
        {
            ChaosPrefab.Panel(parent, UIAnchor.TopStretch, new Offset(5f, -35f, -5f, -5f))
                .WithChildren(titleBar =>
                {
                    ChaosPrefab.Title(titleBar, UIAnchor.CenterLeft, new Offset(5f, -15f, 205f, 15f), Lang("UI.Title", player))
                        .WithOutline(ChaosStyle.BlackOutline);

                    ChaosPrefab.CloseButton(titleBar, UIAnchor.CenterRight, new Offset(-25f, -10f, -5f, 10f), m_OutlineClose)
                        .WithCallback(m_CallbackHandler, arg =>
                        {
                            ClearUiState(player);
                            ChaosUI.Destroy(player, TPUI);
                        }, $"{player.UserIDString}.menu.exit");
                });
        }

        private void CreateModeSelector(BasePlayer player, BaseContainer parent, UiMode mode)
        {
            ImageContainer.Create(parent, UIAnchor.TopCenter, new Offset(-150f, 0f, 150f, 25f))
                .WithStyle(m_StylePreset.Background)
                .WithSprite(Sprites.Background_Rounded_top)
                .WithChildren(modeSelector =>
                {
                    bool canTeleport = HasPerm(player, "teleportgui.tp.use");
                    bool canHome = HasPerm(player, "teleportgui.homes.use");
                    bool canWarp = HasPerm(player, "teleportgui.warps.use");
                    
                    ImageContainer.Create(modeSelector, UIAnchor.Center, new Offset(-145f, -12.5f, -51.66666f, 7.5f))
                        .WithStyle(mode == UiMode.Teleport ? m_StylePreset.Header : (canTeleport ? m_StylePreset.Button : m_StylePreset.DisabledButton))
                        .WithSprite(Sprites.Background_Rounded)
                        .WithChildren(button =>
                        {
                            TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                .WithText(Lang("UI.Teleport", player))
                                .WithStyle(canTeleport ? m_StylePreset.Button : m_StylePreset.DisabledButton);

                            if (canTeleport)
                            {
                                ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg => ShowTeleportUI(player, UiMode.Teleport), $"{player.UserIDString}.mode.teleport");
                            }
                        });

                    ImageContainer.Create(modeSelector, UIAnchor.Center, new Offset(-46.66666f, -12.5f, 46.66667f, 7.5f))
                        .WithStyle(mode == UiMode.Home ? m_StylePreset.Header : (canHome ? m_StylePreset.Button : m_StylePreset.DisabledButton))
                        .WithSprite(Sprites.Background_Rounded)
                        .WithChildren(button =>
                        {
                            TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                .WithText(Lang("UI.Homes", player))
                                .WithStyle(canHome ? m_StylePreset.Button : m_StylePreset.DisabledButton);

                            if (canHome)
                            {
                                ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg => ShowTeleportUI(player, UiMode.Home), $"{player.UserIDString}.mode.home");
                            }
                        });

                    ImageContainer.Create(modeSelector, UIAnchor.Center, new Offset(51.66667f, -12.5f, 145f, 7.5f))
                        .WithStyle(mode == UiMode.Warp ? m_StylePreset.Header : (canWarp ? m_StylePreset.Button : m_StylePreset.DisabledButton))
                        .WithSprite(Sprites.Background_Rounded)
                        .WithChildren(button =>
                        {
                            TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                .WithText(Lang("UI.Warps", player))
                                .WithStyle(canWarp ? m_StylePreset.Button : m_StylePreset.DisabledButton);

                            if (canWarp)
                            {
                                ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg => ShowTeleportUI(player, UiMode.Warp), $"{player.UserIDString}.mode.warp");
                            }
                        });
                });
        }

        private void CreateHeaderBar(BasePlayer player, TeleportGUIData.UserData userData, BaseContainer parent, bool hasNextPage, UiMode mode)
        {
            ChaosPrefab.Panel(parent, UIAnchor.TopStretch, new Offset(5f, -70f, -5f, -40f))
                .WithChildren(header =>
                {
                    // Pagination
                    ChaosPrefab.PreviousPage(header, UIAnchor.CenterLeft, new Offset(5f, -10f, 35f, 10f), GetUiPage(player) > 0)?
                        .WithCallback(m_CallbackHandler, arg =>
                        {
                            SetUiPage(player, GetUiPage(player) - 1);
                            ShowTeleportUI(player, mode);
                        }, $"{player.UserIDString}.back");

                    ChaosPrefab.NextPage(header, UIAnchor.CenterRight, new Offset(-35f, -10f, -5f, 10f), hasNextPage)?
                        .WithCallback(m_CallbackHandler, arg =>
                        {
                            SetUiPage(player, GetUiPage(player) + 1);
                            ShowTeleportUI(player, mode);
                        }, $"{player.UserIDString}.next");

                    // Search Input
                    ChaosPrefab.Input(header, UIAnchor.CenterRight, new Offset(-240f, -10f, -40f, 10f), GetUiSearch(player))
                        .WithCallback(m_CallbackHandler, arg =>
                            {
                                SetUiSearch(player, arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty);
                                ShowTeleportUI(player, mode);
                            }, $"{player.UserIDString}.search");

                    if (!string.IsNullOrEmpty(m_MagnifyImage))
                    {
                        RawImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-265f, -10f, -245f, 10f))
                            .WithPNG(m_MagnifyImage);
                    }

                    if (mode == UiMode.Teleport)
                    {
                        if (HasPerm(player, "teleportgui.tp.autoaccept") || HasPerm(player, "teleportgui.tp.sleepers"))
                        {
                            ChaosPrefab.SpriteButton(header, UIAnchor.CenterLeft, new Offset(40f, -10f, 60f, 10f),
                                    Icon.Gear, UIAnchor.FullStretch, new Offset(2f, 2f, -2f, -2f))
                                .WithCallback(m_CallbackHandler, arg => ShowTeleportSettingsUI(player, userData, mode), $"{player.UserIDString}.settings");
                            /*ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(40f, -10f, 60f, 10f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(settings =>
                                {
                                    ImageContainer.Create(settings, UIAnchor.FullStretch, new Offset(2f, 2f, -2f, -2f))
                                        .WithSprite(Icon.Gear);

                                    ButtonContainer.Create(settings, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg => ShowTeleportSettingsUI(player, userData, mode), $"{player.UserIDString}.settings");
                                });*/
                        }
                    }

                    if (mode == UiMode.Home)
                    {
                        int maxHomes = GetMaxHomesForPlayer(player);
                        bool canSetHome = HasPerm(player, "teleportgui.homes.use") && (maxHomes == 0 || userData.Homes.Count < maxHomes);
                        
                        ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(40f, -10f, 60f, 10f))
                            .WithStyle(canSetHome ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                            .WithChildren(settings =>
                            {
                                ImageContainer.Create(settings, UIAnchor.FullStretch, new Offset(2f, 2f, -2f, -2f))
                                    .WithSprite(Icon.Add)
                                    .WithColor(canSetHome ? Color.White : m_StylePreset.DisabledButton.FontColor);

                                if (canSetHome)
                                {
                                    ButtonContainer.Create(settings, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg => SaveHomeUI(player), $"{player.UserIDString}.addhome");
                                }
                            });
                    }
                    
                    if (mode == UiMode.Warp && HasPerm(player, "teleportgui.warps.admin"))
                    {
                        ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(40f, -10f, 60f, 10f))
                            .WithStyle(m_StylePreset.Button)
                            .WithChildren(settings =>
                            {
                                ImageContainer.Create(settings, UIAnchor.FullStretch, new Offset(2f, 2f, -2f, -2f))
                                    .WithSprite(Icon.Add);

                                ButtonContainer.Create(settings, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg => SaveWarpUI(player), $"{player.UserIDString}.addwarp");
                            });
                    }
                });
        }

        private void CreateGridLayout<T>(BasePlayer player, TeleportGUIData.UserData userData, BaseContainer parent, List<T> list, UiMode mode, Action<BaseContainer, UIAnchor, Offset, T> createAction)
        {
            ChaosPrefab.Panel(parent, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -75f))
                .WithChildren(grid =>
                {
                    TeleportGUIConfig.LimitOptions limits = mode == UiMode.Warp ? _config.Warp.Limits :
                                                     mode == UiMode.Home ? _config.Home.Limits :
                                                     _config.Teleport.Limits;
                    
                    TeleportGUIConfig.PurchaseOptions purchase = mode == UiMode.Warp ? _config.Warp.Purchase :
                                                          mode == UiMode.Home ? _config.Home.Purchase :
                                                          _config.Teleport.Purchase;
                    
                    TeleportGUIData.UserData.Usage usage = mode == UiMode.Warp ? userData.WarpUsage :
                                                    mode == UiMode.Home ? userData.HomeUsage :
                                                    userData.TPUsage;
                    
                    bool isOnCooldown = usage.IsOnCooldown(CurrentTime());
                    bool notRestrictedByLimit = true;
                    bool hasPendingRequest = HasPendingTpActivity(player);
                    bool canTeleport = !isOnCooldown && !hasPendingRequest && notRestrictedByLimit;
                    
                    ImageContainer.Create(grid, UIAnchor.TopStretch, new Offset(0f, -20f, 0f, 0f))
                        .WithStyle(m_StylePreset.Header)
                        .WithChildren(header =>
                        {
                            if (limits.Default > 0 || purchase.PayAlways)
                            {
                                if (HasReachedDailyLimit(player, userData, PaymentKindFromUiMode(mode)) || purchase.PayAlways)
                                {
                                    if (purchase.PayAfterUsingDailyLimits || purchase.PayAlways)
                                    {
                                        TextContainer.Create(header, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                            .WithText(Lang("UI.CostToTP", player, purchase.GetLowestOption(p => HasVipPermission(player, p)), Lang($"PurchaseMode.{purchase.Mode}", player)))
                                            .WithAlignment(TextAnchor.MiddleLeft);
                                    }
                                    else
                                    {
                                        notRestrictedByLimit = false;
                                        TextContainer.Create(header, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                            .WithText(Lang("UI.TPLimit", player))
                                            .WithAlignment(TextAnchor.MiddleLeft);
                                    }
                                }
                                else
                                {
                                    int limit = limits.GetHighestOption(p => HasVipPermission(player, p));
                                    
                                    TextContainer.Create(header, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                        .WithText(Lang("UI.DailyLimitRemain", player, limit == 0 ? Lang("UI.Unlimited2", player) : limit - usage.UsesToday))
                                        .WithAlignment(TextAnchor.MiddleLeft);
                                    
                                }
                            }

                            if (isOnCooldown)
                            {
                                TextContainer.Create(header, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                    .WithText(Lang("UI.CooldownRemain", player))
                                    .WithAlignment(TextAnchor.MiddleRight)
                                    .WithCountdown(new CountdownComponent((int)(usage.Cooldown - CurrentTime())));
                                
                                // Temp solution for broken countdown component
                                /*string guid = CuiHelper.GetGuid();
                                int time = (int)(usage.Cooldown - CurrentTime());
                                
                                TextContainer.Create(header, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                    .WithText(Lang("UI.CooldownRemain", player).Replace("%TIME_LEFT%", time.ToString()))
                                    .WithAlignment(TextAnchor.MiddleRight)
                                    .WithName(guid);
                                    
                                ServerCountdown.Add(player, guid, "UI.CooldownRemain", time);*/
                            }
                        });

                    if (list.Count == 0)
                    {
                        TextContainer.Create(grid, UIAnchor.FullStretch, Offset.zero)
                            .WithText(Lang(mode == UiMode.Home ? "UI.NoHomes" : mode == UiMode.Warp ? "UI.NoWarps" : "UI.NoPlayers", player))
                            .WithAlignment(TextAnchor.MiddleCenter);
                    }
                    BaseContainer.Create(grid, UIAnchor.FullStretch, new Offset(0f, 0f, 0f, -20f))
                        .WithLayoutGroup(m_GridLayout, list, GetUiPage(player), (i, t, layout, anchor, offset) =>
                        {
                            createAction(layout, anchor, offset, t);
                        });
                });
        }

        private void CreatePlayerEntry(BasePlayer player, TeleportGUIData.UserData userData, BaseContainer layout, UIAnchor anchor, Offset offset, BasePlayer t)
        {
            bool isOnCooldown = userData.TPUsage.IsOnCooldown(CurrentTime());
            bool hasPendingRequest = HasPendingTpActivity(player);
            bool canTeleport = !isOnCooldown && !hasPendingRequest;

            bool canTeleportToPlayer = canTeleport && (t.IsConnected || ((player.IsAdmin || (_config.Admin?.Instant == true && player.IsAdmin)) && _config.Admin.Instant));

            ChaosPrefab.Panel(layout, anchor, offset)
                .WithChildren(template =>
                {
                    TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 0f, -85f, 0f))
                        .WithText(t.displayName.StripTags())
                        .WithAlignment(TextAnchor.MiddleLeft);

                    ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-41f, 1f, -1f, -1f))
                        .WithStyle(canTeleportToPlayer ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                        .WithChildren(tpto =>
                        {
                            TextContainer.Create(tpto, UIAnchor.FullStretch, Offset.zero)
                                .WithStyle(canTeleportToPlayer ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                .WithSize(12)
                                .WithText(Lang("UI.TPR", player))
                                .WithAlignment(TextAnchor.MiddleCenter);

                            if (canTeleportToPlayer)
                            {
                                ButtonContainer.Create(tpto, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        ClearUiState(player);
                                        ChaosUI.Destroy(player, TPUI);
                                        CmdTpr(player, new[] { t.UserIDString }, false);
                                    }, $"{player.UserIDString}.tpr.{t.UserIDString}");
                            }

                        });

                    if (HasPerm(player, "teleportgui.tp.tphere"))
                    {
                        ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-86f, 1f, -46f, -1f))
                            .WithStyle(canTeleportToPlayer ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                            .WithChildren(tphere =>
                            {
                                TextContainer.Create(tphere, UIAnchor.FullStretch, Offset.zero)
                                    .WithStyle(canTeleportToPlayer ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                    .WithSize(12)
                                    .WithText(Lang("UI.TPHere", player))
                                    .WithAlignment(TextAnchor.MiddleCenter);

                                if (canTeleportToPlayer)
                                {
                                    ButtonContainer.Create(tphere, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            ClearUiState(player);
                                            ChaosUI.Destroy(player, TPUI);
                                            CmdTpr(player, new[] { t.UserIDString }, true);
                                        }, $"{player.UserIDString}.tphere.{t.UserIDString}");
                                }
                            });
                    }
                });
        }
        
        private void CreateHomeEntry(BasePlayer player, TeleportGUIData.UserData userData, BaseContainer layout, UIAnchor anchor, Offset offset, KeyValuePair<string, TeleportGUIData.UserData.HomePoint> t)
        {
            ButtonContainer.Create(layout, anchor, offset)
                .WithCallback(m_CallbackHandler, arg =>
                {
                    bool isOnCooldown = userData.HomeUsage.IsOnCooldown(CurrentTime());
                    bool hasPendingRequest = HasPendingTpActivity(player);
                    
                    if (!isOnCooldown && !hasPendingRequest)
                    {
                        if (!t.Value.TryGetPosition(out Vector3 position))
                        {
                            userData.Homes.Remove(t.Key);
                            SendLang(player, "Home.Error.NoEntity", t.Key);
                            ShowTeleportUI(player, UiMode.Home);
                            return;
                        }
                        
                        if (IsInvalidBagSpawn(t.Value, player, out InvalidBagReason reason))
                        {
                            userData.Homes.Remove(t.Key);
                    
                            string message = reason switch
                            {
                                InvalidBagReason.IsPublic => "Home.Error.BagPublic",
                                InvalidBagReason.NotAssigned => "Home.Error.BagNotAssigned",
                                _ => "Home.Error.NoEntity"
                            };
                            SendLang(player, message, t.Key);
                            ShowTeleportUI(player, UiMode.Home);
                            return;
                        }
                        
                        if (!IsHomePointValid(t.Value))
                        {
                            SendLang(player, "Home.Error.Invalid", t.Key);

                            userData.Homes.Remove(t.Key);
                            ShowTeleportUI(player, UiMode.Home);
                            return;
                        }

                        if (t.Value.EntityID != 0UL)
                            position += Vector3.up * 0.55f;
                        
                        if (IsInsideEntity(position))
                        {
                            SendLang(player, "Home.Error.Blocked", t.Key);
                            return;
                        }
                        
                        if (MeetsPositionConditions(player, position, false))
                        {
                            ChaosUI.Destroy(player, TPUI);
                            ClearUiState(player);
                            CmdHome(player, "home", new[] { t.Key });
                        }
                    }
                }, $"{player.UserIDString}.home.{t.Key}")
                .WithStyle(m_StylePreset.Panel)
                .WithChildren(template =>
                {
                    TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                        .WithText(t.Key)
                        .WithAlignment(TextAnchor.MiddleCenter);

                    ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-20f, 2f, -2f, -2f))
                        .WithStyle(m_StylePreset.Button)
                        .WithOutline(m_OutlineClose)
                        .WithChildren(delete =>
                        {
                            ImageContainer.Create(delete, UIAnchor.FullStretch, new Offset(2f, 2f, -2f, -2f))
                                .WithSprite(Icon.Clear);
                            
                            ButtonContainer.Create(delete, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    userData.Homes.Remove(t.Key);
                                    SendLang(player, "Home.Success.Deleted", t.Key);
                                    ShowTeleportUI(player, UiMode.Home);
                                }, $"{player.UserIDString}.deletehome.{t.Key}");
                        });
                });
        }
        
        private void CreateWarpEntry(BasePlayer player, TeleportGUIData.UserData userData, BaseContainer layout, UIAnchor anchor, Offset offset, KeyValuePair<string, TeleportGUIData.WarpPoint> t, bool isManual)
        {
            bool canWarp = string.IsNullOrWhiteSpace(t.Value?.Permission) ||
                           HasPerm(player, EnsureWarpPermission(t.Value.Permission));
            
            ButtonContainer.Create(layout, anchor, offset)
                .WithCallback(m_CallbackHandler, arg =>
                {
                    if (!canWarp)
                    {
                        SendLang(player, "Warp.Error.NoPermission");
                        return;
                    }
                    
                    bool isOnCooldown = userData.WarpUsage.IsOnCooldown(CurrentTime());
                    bool hasPendingRequest = HasPendingTpActivity(player);
                    bool canTeleport = !isOnCooldown && !hasPendingRequest;
                    
                    if (canTeleport)
                    {
                        ChaosUI.Destroy(player, TPUI);
                        ClearUiState(player);
                        CmdWarp(player, new[] { t.Key });
                    }
                }, $"{player.UserIDString}.warp.{t.Key}")
                .WithStyle(m_StylePreset.Panel)
                .WithChildren(template =>
                {
                    TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                        .WithText(t.Key)
                        .WithAlignment(TextAnchor.MiddleCenter)
                        .WithColor(canWarp ? Color.White : m_StylePreset.DisabledButton.FontColor);

                    if (HasPerm(player, "teleportgui.warps.admin") && isManual)
                    {
                        ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-20f, 2f, -2f, -2f))
                            .WithStyle(m_StylePreset.Button)
                            .WithOutline(m_OutlineClose)
                            .WithChildren(delete =>
                            {
                                ImageContainer.Create(delete, UIAnchor.FullStretch, new Offset(2f, 2f, -2f, -2f))
                                    .WithSprite(Icon.Clear);
                            
                                ButtonContainer.Create(delete, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        if (_warpData != null && _warpData.Remove(t.Key))
                                        {
                                            _data.WarpPoints = _warpData;
                                            SaveData();
                                            RegisterWarpChatCommands();
                                        }
                                        SendLang(player, "WarpRemove.Success", t.Key);
                                        ShowTeleportUI(player, UiMode.Warp);
                                    }, $"{player.UserIDString}.deletewarp.{t.Key}");
                            });
                    }
                });
        }

        private List<BasePlayer> BuildPlayerList(BasePlayer player, TeleportGUIData.UserData userData)
        {
            List<BasePlayer> list = Pool.Get<List<BasePlayer>>();
            list.AddRange(BasePlayer.activePlayerList);
                 
            if (userData.ShowSleepers)
                list.AddRange(BasePlayer.sleepingPlayerList);

            if (_config.UI.HideAdminsInUI && !(player.IsAdmin || (_config.Admin?.Instant == true && player.IsAdmin)))
                list.RemoveAll(p => p != null && p.IsAdmin);

            list.RemoveAll(x => HasPerm(x, "teleportgui.hideingui"));

            list.Remove(player);
            
            if (!HasPerm(player, "teleportgui.seeall") && _config.Teleport?.FriendliesOnly == true)
            {
                list = list.Where(p =>
                    p != null && (
                        (player.currentTeam != 0UL && player.currentTeam == p.currentTeam) ||
                        TeleportGUIIntegrations.Clans.IsClanMember(player.userID, p.userID) ||
                        TeleportGUIIntegrations.Friends.AreFriends(player.userID, p.userID))).ToList();
            }

            if (!string.IsNullOrEmpty(GetUiSearch(player)))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    BasePlayer p = list[i];
                    if (!p.displayName.Contains(GetUiSearch(player), CompareOptions.OrdinalIgnoreCase))
                        list.RemoveAt(i);
                }
            }

            list.Sort((a,b) => a.displayName.CompareTo(b.displayName));
            return list;
        }
        
        private readonly struct UiWarpRow
        {
            public readonly string Name;
            public readonly TeleportGUIData.WarpPoint Point;
            public readonly bool IsManual;
            public UiWarpRow(string name, TeleportGUIData.WarpPoint point, bool isManual)
            {
                Name = name; Point = point; IsManual = isManual;
            }
        }

        private List<KeyValuePair<string, TeleportGUIData.UserData.HomePoint>> BuildHomeList(BasePlayer player, TeleportGUIData.UserData userData)
        {
            List<KeyValuePair<string, TeleportGUIData.UserData.HomePoint>> list = Pool.Get<List<KeyValuePair<string, TeleportGUIData.UserData.HomePoint>>>();
            list.AddRange(userData.Homes);
            
            if (!string.IsNullOrEmpty(GetUiSearch(player)))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    KeyValuePair<string, TeleportGUIData.UserData.HomePoint> p = list[i];
                    if (!p.Key.Contains(GetUiSearch(player), CompareOptions.OrdinalIgnoreCase))
                        list.RemoveAt(i);
                }
            }
            
            list.Sort((a,b) => a.Key.CompareTo(b.Key));
            return list;
        }

        private List<UiWarpRow> BuildWarpList(BasePlayer player)
        {
            var list = Pool.Get<List<UiWarpRow>>();
            foreach (var kvp in EnumerateAllWarps().OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                bool permitted = string.IsNullOrWhiteSpace(kvp.Value?.Permission) ||
                                 HasPerm(player, EnsureWarpPermission(kvp.Value.Permission));
                if (_config.UI?.HideWarpsNoPermission == true && !permitted)
                    continue;
                bool isManual = _warpData != null && _warpData.ContainsKey(kvp.Key);
                list.Add(new UiWarpRow(kvp.Key, kvp.Value, isManual));
            }

            string search = GetUiSearch(player);
            if (!string.IsNullOrEmpty(search))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                        list.RemoveAt(i);
                }
            }

            return list;
        }

        private void SaveHomeUI(BasePlayer player, string homeName = "")
        {
            BaseContainer root = ChaosPrefab.Background(TPUI, Layer.Overall, UIAnchor.FullStretch, Offset.zero, m_StylePreset)
                .WithChildren(parent =>
                {
                    ChaosPrefab.Panel(parent, UIAnchor.Center, new Offset(-150f, -27.5f, 150f, 27.5f))
                        .WithChildren(titleBar =>
                        {
                            TextContainer.Create(titleBar, UIAnchor.TopStretch, new Offset(5f, -25f, -5f, -5f))
                                .WithText(Lang("UI.HomeName", player))
                                .WithAlignment(TextAnchor.MiddleLeft);
                            
                            ChaosPrefab.Input(titleBar, UIAnchor.TopStretch, new Offset(80f, -25f, -5.000015f, -5f), homeName)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    SaveHomeUI(player, arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty);
                                }, $"{player.UserIDString}.homenameinput");

                            ChaosPrefab.TextButton(titleBar, UIAnchor.BottomStretch, new Offset(5f, 5f, -155f, 25f), Lang("UI.Save", player), null, m_OutlineHighlight)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    CmdHome(player, "sethome", new[] { homeName });
                                    ShowTeleportUI(player, UiMode.Home);
                                }, $"{player.UserIDString}.addhome.save");

                            ChaosPrefab.TextButton(titleBar, UIAnchor.BottomStretch, new Offset(155f, 5f, -5f, 25f), Lang("UI.Cancel", player), null, m_OutlineClose)
                                .WithCallback(m_CallbackHandler, arg => { ShowTeleportUI(player, UiMode.Home); }, $"{player.UserIDString}.addhome.cancel");
                        });

                    ChaosPrefab.Panel(parent, UIAnchor.Center, new Offset(-150f, 32.5f, 150f, 52.5f))
                        .WithChildren(infoBar =>
                        {
                            TextContainer.Create(infoBar, UIAnchor.FullStretch, Offset.zero)
                                .WithText(Lang("UI.CreateNewHome", player))
                                .WithAlignment(TextAnchor.MiddleCenter);
                        });
                })
                .DestroyExisting()
                .NeedsCursor()
                .NeedsKeyboard();

            ChaosUI.Show(player, root);
        }

        private void SaveWarpUI(BasePlayer player, string warpName = "", string perm = "", string command = "")
        {
            BaseContainer root = ChaosPrefab.Background(TPUI, Layer.Overall, UIAnchor.FullStretch, Offset.zero, m_StylePreset)
                .WithChildren(parent =>
                {
                    ChaosPrefab.Panel(parent, UIAnchor.Center, new Offset(-150f, -40f, 150f, 65f))
                        .WithChildren(titleBar =>
                        {
                            TextContainer.Create(titleBar, UIAnchor.TopStretch, new Offset(5f, -25f, -5f, -5f))
                                .WithText(Lang("UI.WarpName", player))
                                .WithAlignment(TextAnchor.MiddleLeft);
                            
                            ChaosPrefab.Input(titleBar, UIAnchor.TopStretch, new Offset(80f, -25f, -5.000015f, -5f), warpName)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    SaveWarpUI(player, arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty, perm, command);
                                }, $"{player.UserIDString}.warpnameinput");

                            TextContainer.Create(titleBar, UIAnchor.TopStretch, new Offset(5f, -50f, -5f, -30f))
                                .WithText(Lang("UI.Permission", player))
                                .WithAlignment(TextAnchor.MiddleLeft);
                            
                            ImageContainer.Create(titleBar, UIAnchor.TopStretch, new Offset(80f, -50f, -5f, -30f))
                                .WithStyle(m_StylePreset.Button)
                                .WithChildren(permissionInput =>
                                {
                                    TextContainer.Create(permissionInput, UIAnchor.FullStretch, new Offset(5f, 0f, 0f, 0f))
                                        .WithText("teleportgui.")
                                        .WithColor(new Color(1, 1, 1, 0.5f))
                                        .WithAlignment(TextAnchor.MiddleLeft);

                                    InputFieldContainer.Create(permissionInput, UIAnchor.FullStretch, new Offset(72f, 0f, 0f, 0f))
                                        .WithText(perm)
                                        .WithAlignment(TextAnchor.MiddleLeft)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            SaveWarpUI(player, warpName, arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty, command);
                                        }, $"{player.UserIDString}.warppermissioninput");
                                });
                            
                            TextContainer.Create(titleBar, UIAnchor.TopStretch, new Offset(5f, -75f, -5f, -55f))
                                .WithText(Lang("UI.Command", player))
                                .WithAlignment(TextAnchor.MiddleLeft);
                            
                            ImageContainer.Create(titleBar, UIAnchor.TopStretch, new Offset(80f, -75f, -5f, -55f))
                                .WithStyle(m_StylePreset.Button)
                                .WithChildren(permissionInput =>
                                {
                                    TextContainer.Create(permissionInput, UIAnchor.FullStretch, new Offset(5f, 0f, 0f, 0f))
                                        .WithText("/")
                                        .WithColor(new Color(1, 1, 1, 0.5f))
                                        .WithAlignment(TextAnchor.MiddleLeft);

                                    InputFieldContainer.Create(permissionInput, UIAnchor.FullStretch, new Offset(11f, 0f, 0f, 0f))
                                        .WithText(command)
                                        .WithAlignment(TextAnchor.MiddleLeft)
                                        .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                SaveWarpUI(player, warpName, perm, arg.Args.Length > 1 ? arg.GetString(1) : string.Empty);
                                            }, $"{player.UserIDString}.warpcommandinput");
                                });

                            ChaosPrefab.TextButton(titleBar, UIAnchor.BottomStretch, new Offset(5f, 5f, -155f, 25f), Lang("UI.Save", player), null, m_OutlineHighlight)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    CmdWarpAdd(player, new[] { warpName, perm, command });
                                    ShowTeleportUI(player, UiMode.Warp);
                                }, $"{player.UserIDString}.addwarp.save");

                            ChaosPrefab.TextButton(titleBar, UIAnchor.BottomStretch, new Offset(155f, 5f, -5f, 25f), Lang("UI.Cancel", player), null, m_OutlineClose)
                                .WithCallback(m_CallbackHandler, arg => ShowTeleportUI(player, UiMode.Warp), $"{player.UserIDString}.addwarp.cancel");
                        });

                    ChaosPrefab.Panel(parent, UIAnchor.Center, new Offset(-150f, 70f, 150f, 90f))
                        .WithChildren(infoBar =>
                        {
                            TextContainer.Create(infoBar, UIAnchor.FullStretch, Offset.zero)
                                .WithText(Lang("UI.CreateNewWarp", player))
                                .WithAlignment(TextAnchor.MiddleCenter);
                        });
                })
                .DestroyExisting()
                .NeedsCursor()
                .NeedsKeyboard();

            ChaosUI.Show(player, root);
        }
        
        private void ShowTeleportSettingsUI(BasePlayer player, TeleportGUIData.UserData userData, UiMode mode)
        {
            BaseContainer root = ChaosPrefab.Background(TPUI, Layer.Overall, UIAnchor.FullStretch, Offset.zero, m_StylePreset)
                .WithChildren(parent =>
                {
                    ChaosPrefab.Panel(parent, UIAnchor.Center, new Offset(-100f, -77.5f, 100f, 77.5f))
                        .WithChildren(titleBar =>
                        {
                            ChaosPrefab.TextButton(titleBar, UIAnchor.BottomStretch, new Offset(5f, 5f, -5f, 25f), Lang("UI.Close", player), null, m_OutlineClose)
                                .WithCallback(m_CallbackHandler, arg => ShowTeleportUI(player, mode), $"{player.UserIDString}.settings.close");

                            bool canToggleAutoAccept = HasPerm(player, "teleportgui.tp.autoaccept");
                            
                            ImageContainer.Create(titleBar, UIAnchor.TopLeft, new Offset(5f, -25f, 25f, -5f))
                                .WithStyle(m_StylePreset.Button)
                                .WithChildren(autoaccept =>
                                {
                                    bool isActive = (userData.AutoAccept & TeleportGUIData.UserData.AutoAcceptEnum.Clans) != 0;
                                    if (isActive)
                                    {
                                        ImageContainer.Create(autoaccept, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                                            .WithStyle(m_StylePreset.Toggle);
                                    }

                                    if (canToggleAutoAccept)
                                    {
                                        ButtonContainer.Create(autoaccept, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                if (isActive)
                                                    userData.AutoAccept &= ~TeleportGUIData.UserData.AutoAcceptEnum.Clans;
                                                else userData.AutoAccept |= TeleportGUIData.UserData.AutoAcceptEnum.Clans;

                                                ShowTeleportSettingsUI(player, userData, mode);
                                            }, $"{player.UserIDString}.settings.aaclan");
                                    }

                                    TextContainer.Create(autoaccept, UIAnchor.HoriztonalCenterStretch, new Offset(25f, -10f, 170f, 10f))
                                        .WithText(Lang("UI.AA.Clan", player))
                                        .WithStyle(canToggleAutoAccept ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                        .WithAlignment(TextAnchor.MiddleLeft);
                                });

                            ImageContainer.Create(titleBar, UIAnchor.TopLeft, new Offset(5f, -50f, 25f, -30f))
                                .WithStyle(canToggleAutoAccept ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                .WithChildren(autoaccept =>
                                {
                                    bool isActive = (userData.AutoAccept & TeleportGUIData.UserData.AutoAcceptEnum.Friends) != 0;
                                    if (isActive)
                                    {
                                        ImageContainer.Create(autoaccept, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                                            .WithStyle(m_StylePreset.Toggle);
                                    }

                                    if (canToggleAutoAccept)
                                    {
                                        ButtonContainer.Create(autoaccept, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                if (isActive)
                                                    userData.AutoAccept &= ~TeleportGUIData.UserData.AutoAcceptEnum.Friends;
                                                else userData.AutoAccept |= TeleportGUIData.UserData.AutoAcceptEnum.Friends;

                                                ShowTeleportSettingsUI(player, userData, mode);
                                            }, $"{player.UserIDString}.settings.aafriend");
                                    }

                                    TextContainer.Create(autoaccept, UIAnchor.HoriztonalCenterStretch, new Offset(25f, -10f, 170f, 10f))
                                        .WithText(Lang("UI.AA.Friend", player))
                                        .WithStyle(canToggleAutoAccept ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                        .WithAlignment(TextAnchor.MiddleLeft);
                                });

                            ImageContainer.Create(titleBar, UIAnchor.TopLeft, new Offset(5f, -75f, 25f, -55f))
                                .WithStyle(canToggleAutoAccept ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                .WithChildren(autoaccept =>
                                {
                                    bool isActive = (userData.AutoAccept & TeleportGUIData.UserData.AutoAcceptEnum.Teams) != 0;
                                    if (isActive)
                                    {
                                        ImageContainer.Create(autoaccept, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                                            .WithStyle(m_StylePreset.Toggle);
                                    }

                                    if (canToggleAutoAccept)
                                    {
                                        ButtonContainer.Create(autoaccept, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                if (isActive)
                                                    userData.AutoAccept &= ~TeleportGUIData.UserData.AutoAcceptEnum.Teams;
                                                else userData.AutoAccept |= TeleportGUIData.UserData.AutoAcceptEnum.Teams;

                                                ShowTeleportSettingsUI(player, userData, mode);
                                            }, $"{player.UserIDString}.settings.aateam");
                                    }

                                    TextContainer.Create(autoaccept, UIAnchor.HoriztonalCenterStretch, new Offset(25f, -10f, 170f, 10f))
                                        .WithText(Lang("UI.AA.Team", player))
                                        .WithStyle(canToggleAutoAccept ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                        .WithAlignment(TextAnchor.MiddleLeft);
                                });

                            ImageContainer.Create(titleBar, UIAnchor.TopLeft, new Offset(5f, -100f, 25f, -80f))
                                .WithStyle(canToggleAutoAccept ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                .WithChildren(autoaccept =>
                                {
                                    bool isActive = (userData.AutoAccept & TeleportGUIData.UserData.AutoAcceptEnum.All) != 0;
                                    if (isActive)
                                    {
                                        ImageContainer.Create(autoaccept, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                                            .WithStyle(m_StylePreset.Toggle);
                                    }

                                    if (canToggleAutoAccept)
                                    {
                                        ButtonContainer.Create(autoaccept, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                if (isActive)
                                                    userData.AutoAccept &= ~TeleportGUIData.UserData.AutoAcceptEnum.All;
                                                else userData.AutoAccept |= TeleportGUIData.UserData.AutoAcceptEnum.All;

                                                ShowTeleportSettingsUI(player, userData, mode);
                                            }, $"{player.UserIDString}.settings.aaall");
                                    }

                                    TextContainer.Create(autoaccept, UIAnchor.HoriztonalCenterStretch, new Offset(25f, -10f, 170f, 10f))
                                        .WithText(Lang("UI.AA.All", player))
                                        .WithStyle(canToggleAutoAccept ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                        .WithAlignment(TextAnchor.MiddleLeft);
                                });

                            bool canToggleSleepers = HasPerm(player, "teleportgui.tp.sleepers");
                            
                            ImageContainer.Create(titleBar, UIAnchor.TopLeft, new Offset(5f, -125f, 25f, -105f))
                                .WithStyle(canToggleSleepers ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                .WithChildren(showSleepers =>
                                {
                                    if (userData.ShowSleepers)
                                    {
                                        ImageContainer.Create(showSleepers, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                                            .WithStyle(m_StylePreset.Toggle);
                                    }

                                    if (canToggleSleepers)
                                    {
                                        ButtonContainer.Create(showSleepers, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                userData.ShowSleepers = !userData.ShowSleepers;
                                                ShowTeleportSettingsUI(player, userData, mode);
                                            }, $"{player.UserIDString}.settings.sleepers");
                                    }

                                    TextContainer.Create(showSleepers, UIAnchor.HoriztonalCenterStretch, new Offset(25f, -10f, 170f, 10f))
                                        .WithText(Lang("UI.ShowSleepers", player))
                                        .WithStyle(canToggleSleepers ? m_StylePreset.Button : m_StylePreset.DisabledButton)
                                        .WithAlignment(TextAnchor.MiddleLeft);
                                });
                        });

                    ChaosPrefab.Panel(parent, UIAnchor.Center, new Offset(-100f, 82.5f, 100f, 102.5f))
                        .WithChildren(infoBar =>
                        {
                            TextContainer.Create(infoBar, UIAnchor.FullStretch, Offset.zero)
                                .WithText(Lang("UI.TeleportSettings", player))
                                .WithAlignment(TextAnchor.MiddleCenter);
                        });
                })
                .DestroyExisting()
                .NeedsCursor()
                .NeedsKeyboard();


            ChaosUI.Show(player, root);
        }

        #endregion

        public void DestroyTeleportUI(BasePlayer player) => ChaosUI.Destroy(player, TPUI);

        private void TeardownUIComponents()
        {
            if (m_CallbackHandler == null) return;
            m_CallbackHandler.Clear();
            m_CallbackHandler.Unregister();
            m_CallbackHandler = null;
        }

        private string Lang(string key, BasePlayer player, params object[] args) =>
            TeleportGUILanguage.Get(key, player, args);

        private int GetUiPage(BasePlayer player) =>
            _uiState.TryGetValue(player.userID, out var s) ? s.page : 0;

        private string GetUiSearch(BasePlayer player) =>
            _uiState.TryGetValue(player.userID, out var s) ? s.search ?? string.Empty : string.Empty;

        private void SetUiPage(BasePlayer player, int page)
        {
            if (!_uiState.TryGetValue(player.userID, out var s))
                s = ("teleport", 0, string.Empty);
            _uiState[player.userID] = (s.mode, Math.Max(0, page), s.search);
        }

        private void SetUiSearch(BasePlayer player, string search)
        {
            if (!_uiState.TryGetValue(player.userID, out var s))
                s = ("teleport", 0, string.Empty);
            _uiState[player.userID] = (s.mode, 0, search ?? string.Empty);
        }

        private void ClearUiState(BasePlayer player)
        {
            _uiState.Remove(player.userID);
            _showingModal.Remove(player.userID);
            _pendingWarpPosition.Remove(player.userID);
            _pendingWarpForms.Remove(player.userID);
        }

        private bool HasPendingTpActivity(BasePlayer player) =>
            _outgoingRequests.ContainsKey(player.userID) ||
            _incomingRequests.ContainsKey(player.userID) ||
            _playersInDelayedTeleport.ContainsKey(player.userID);

        private static bool IsAdminPlayer(BasePlayer player) => player != null && player.IsAdmin;
    }
}
