using System.Collections.Generic;
using Facepunch;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;
using UnityEngine.UI;

using Color = Oxide.Ext.Chaos.UIFramework.Color;
using Font = Oxide.Ext.Chaos.UIFramework.Font;
using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;

namespace DynamicCupShareHarmony
{
    public partial class DynamicCupSharePlugin
    {
        private const string UI_MENU = "dcs.menu";

        private enum ShareUiPage { Sharing, Commands }

        private Style _backgroundStyle;
        private Style _panelStyle;
        private Style _buttonStyle;
        private Style _titleStyle;
        private Style _closeStyle;
        private Style _labelStyle;

        private Color _toggleColor;
        private OutlineComponent _outlineGreen;
        private OutlineComponent _outlineRed;

        private CommandCallbackHandler _callbackHandler;
        private readonly Dictionary<string, string> _uiInputs = new Dictionary<string, string>();

        private void SetupUIComponents()
        {
            _callbackHandler = new CommandCallbackHandler(this);

            _backgroundStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Background.Hex, Configuration.Colors.Background.Alpha),
                Material = Materials.BackgroundBlur,
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled
            };

            _panelStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Panel.Hex, Configuration.Colors.Panel.Alpha),
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled
            };

            _buttonStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Button.Hex, Configuration.Colors.Button.Alpha),
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled,
                Alignment = TextAnchor.MiddleCenter,
                FontSize = 14
            };

            _titleStyle = new Style
            {
                FontSize = 18,
                Font = Font.PermanentMarker,
                Alignment = TextAnchor.MiddleLeft,
                WrapMode = VerticalWrapMode.Overflow
            };

            _closeStyle = new Style
            {
                FontSize = 18,
                Alignment = TextAnchor.MiddleCenter,
                WrapMode = VerticalWrapMode.Overflow,
            };

            _labelStyle = new Style
            {
                FontSize = 12,
                Alignment = TextAnchor.MiddleLeft,
                WrapMode = VerticalWrapMode.Truncate
            };

            _toggleColor = new Color(Configuration.Colors.Highlight.Hex, Configuration.Colors.Highlight.Alpha);
            _outlineGreen = new OutlineComponent(new Color(Configuration.Colors.Highlight.Hex, Configuration.Colors.Highlight.Alpha));
            _outlineRed = new OutlineComponent(new Color(Configuration.Colors.Close.Hex, Configuration.Colors.Close.Alpha));
        }

        private void OpenShareMenu(BasePlayer player, StoredData.PlayerData playerData, ulong shareTarget, ShareUiPage page = ShareUiPage.Sharing)
        {
            if (player == null || playerData == null) return;

            if (page == ShareUiPage.Commands)
                OpenCommandsMenu(player, playerData, shareTarget);
            else
                OpenSharingMenu(player, playerData, shareTarget);
        }

        private void OpenSharingMenu(BasePlayer player, StoredData.PlayerData playerData, ulong shareTarget)
        {
            List<TeamType> list = Facepunch.Pool.Get<List<TeamType>>();

            ulong subjectId = shareTarget != 0UL ? shareTarget : player.GetUserId();

            if (CanShare(TeamType.Clan, subjectId))
                list.Add(TeamType.Clan);
            if (CanShare(TeamType.Friend, subjectId))
                list.Add(TeamType.Friend);
            if (CanShare(TeamType.Team, subjectId))
                list.Add(TeamType.Team);

            if (list.Count == 0)
            {
                Facepunch.Pool.FreeUnmanaged(ref list);
                OpenCommandsMenu(player, playerData, shareTarget);
                return;
            }

            float width = Mathf.Max(250, list.Count * 130);
            float halfWidth = width * 0.5f;

            float height = 40 + (25 * (AllowedShareTypes.Count + 1)) + 5;
            float halfHeight = height * 0.5f;

            float shareWidth = list.Count > 0 ? width / list.Count : width;

            BaseContainer root = ImageContainer.Create(UI_MENU, Layer.Overall, UIAnchor.Center, new Offset(-halfWidth, -halfHeight, halfWidth, halfHeight))
                .WithStyle(_backgroundStyle)
                .NeedsCursor()
                .DestroyExisting()
                .WithChildren(parent =>
                {
                    CreateMenuHeader(parent, player, playerData, shareTarget, ShareUiPage.Sharing, width);

                    for (int i = 0; i < list.Count; i++)
                    {
                        TeamType teamType = list[i];
                        Offset offset = new Offset(-halfWidth + (shareWidth * i), -halfHeight, -halfWidth + (shareWidth * (i + 1)), halfHeight - 40);

                        BaseContainer.Create(parent, UIAnchor.Center, offset)
                            .WithChildren(column =>
                            {
                                ImageContainer.Create(column, UIAnchor.FullStretch, new Offset(list.Count > 1 && i > 0 ? 2.5f : 5, 5, i < list.Count - 1 ? -2.5f : -5, 0))
                                    .WithStyle(_panelStyle)
                                    .WithChildren(type =>
                                    {
                                        TextContainer.Create(type, UIAnchor.TopStretch, new Offset(0, -25, 0, 0))
                                            .WithText(GetString($"UI.Share.{teamType}", player))
                                            .WithStyle(_buttonStyle);

                                        for (int j = 0; j < AllowedShareTypes.Count; j++)
                                        {
                                            ShareType shareType = AllowedShareTypes[j];
                                            int index = j + 1;
                                            bool isSharing = playerData.IsSharing(teamType, shareType);

                                            BaseContainer button = ImageContainer.Create(type, UIAnchor.TopStretch, new Offset(5, -((20 * (index + 1)) + (5 * index)), -5, -(25 * index)))
                                                .WithStyle(_buttonStyle);

                                            TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                                .WithText(GetString($"UI.Type.{shareType}", player))
                                                .WithStyle(_buttonStyle);

                                            ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                                .WithColor(Color.Clear)
                                                .WithCallback(_callbackHandler, arg =>
                                                {
                                                    if (!isSharing)
                                                        playerData.Share(teamType, shareType);
                                                    else
                                                    {
                                                        if (shareType == ShareType.Turret && Configuration.Security.TurretShareOverride)
                                                        {
                                                            Message(player, "Chat.NoTurretToggle");
                                                            return;
                                                        }

                                                        playerData.Unshare(teamType, shareType);
                                                    }

                                                    PlayerEntities.GetOrCreate(shareTarget != 0UL ? shareTarget : player.GetUserId())?.OnToggleShareType(shareType);

                                                    player.ChatMessage(string.Format(
                                                        !isSharing ? GetString("Chat.ShareEnabled", player) : GetString("Chat.ShareDisabled", player),
                                                        shareType, teamType));

                                                    OpenShareMenu(player, playerData, shareTarget, ShareUiPage.Sharing);
                                                }, $"{player.UserIDString}.{teamType}.{shareType}");

                                            if (isSharing)
                                                button.WithOutline(_outlineGreen);
                                        }
                                    });
                            });
                    }
                });

            Facepunch.Pool.FreeUnmanaged(ref list);
            ChaosUI.Show(player, root);
        }

        private void OpenCommandsMenu(BasePlayer player, StoredData.PlayerData playerData, ulong shareTarget)
        {
            const float WIDTH = 380f;
            const float ROW = 30f;
            const float HEADER = 40f;

            bool canBpToggle = HasBlueprintPermission(player, Configuration.Permission.BlueprintToggle);
            bool canBpShare = HasBlueprintPermission(player, Configuration.Permission.BlueprintShare);
            bool canBpShow = HasBlueprintPermission(player, Configuration.Permission.BlueprintShow)
                             && Configuration.Blueprints != null
                             && Configuration.Blueprints.LoseBlueprintsOnLeave;
            bool canAdmin = player.HasPermission(Configuration.Permission.AdminPermission);
            bool canSharePlayer = player.IsAdmin;
            bool showBlueprints = BlueprintSharingAllowed();
            bool canBuildingWorkbench = BuildingWorkbenchFeatureEnabled
                                        && (string.IsNullOrEmpty(Configuration.Permission.BuildingWorkbenchUse)
                                            || player.HasPermission(Configuration.Permission.BuildingWorkbenchUse));

            int rows = 3;
            if (showBlueprints)
            {
                rows += 1;
                if (canBpShare) rows += 1;
                if (canBpShow) rows += 2;
            }
            if (canBuildingWorkbench) rows += 1;
            if (canAdmin) rows += 1;
            if (canSharePlayer) rows += 1;

            float height = HEADER + 10f + (rows * ROW);
            float halfWidth = WIDTH * 0.5f;
            float halfHeight = height * 0.5f;

            BaseContainer root = ImageContainer.Create(UI_MENU, Layer.Overall, UIAnchor.Center, new Offset(-halfWidth, -halfHeight, halfWidth, halfHeight))
                .WithStyle(_backgroundStyle)
                .NeedsCursor()
                .NeedsKeyboard()
                .DestroyExisting()
                .WithChildren(parent =>
                {
                    CreateMenuHeader(parent, player, playerData, shareTarget, ShareUiPage.Commands, WIDTH);

                    ImageContainer.Create(parent, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -40f))
                        .WithStyle(_panelStyle)
                        .WithChildren(layout =>
                        {
                            int index = 0;

                            CreateInfoRow(layout, player, "UI.Commands.Intro", ++index);

                            CreateActionRow(layout, player,
                                string.Format(GetString("UI.Commands.Share", player), Configuration.Sharing.ChatCommand),
                                GetString("UI.Commands.Share.Action", player),
                                ++index,
                                () => OpenShareMenu(player, playerData, shareTarget, ShareUiPage.Sharing));

                            string bpCmd = Configuration.Blueprints?.ChatCommand ?? "bs";
                            CreateInfoRow(layout, player, string.Format(GetString("UI.Commands.Bs", player), bpCmd), ++index);

                            if (showBlueprints)
                            {
                                if (canBpToggle)
                                {
                                    bool bpOn = IsAnyBlueprintSharing(player.GetUserId(), playerData);
                                    CreateToggleRow(layout, player,
                                        string.Format(GetString("UI.Commands.BsToggle", player), bpCmd),
                                        bpOn, ++index,
                                        () =>
                                        {
                                            CmdBlueprintToggle(player);
                                            OpenShareMenu(player, storedData.SetupPlayer(player.GetUserId()), shareTarget, ShareUiPage.Commands);
                                        });
                                }
                                else
                                    CreateInfoRow(layout, player, string.Format(GetString("UI.Commands.BsToggle", player), bpCmd), ++index);

                                if (canBpShare)
                                {
                                    CreateInputRow(layout, player,
                                        string.Format(GetString("UI.Commands.BsShare", player), bpCmd),
                                        GetString("UI.Commands.PlayerPlaceholder", player),
                                        GetString("UI.Commands.Go", player),
                                        ++index, $"{player.UserIDString}.bpshare",
                                        value => CmdBlueprintShareWith(player, new[] { "share", value }));
                                }

                                if (canBpShow)
                                {
                                    CreateShowBlueprintRow(layout, player, bpCmd, ++index);
                                    CreateInputRow(layout, player,
                                        GetString("UI.Commands.BsShowFriend", player),
                                        GetString("UI.Commands.FriendPlaceholder", player),
                                        GetString("UI.Commands.Friend", player),
                                        ++index, $"{player.UserIDString}.bpshowfriend",
                                        value => CmdBlueprintShow(player, new[] { "show", "friend", value }));
                                }
                            }

                            if (canBuildingWorkbench)
                            {
                                CreateToggleRow(layout, player,
                                    GetString("UI.Commands.BuildingWorkbench", player),
                                    playerData.BuildingWorkbenchEnabled, ++index,
                                    () =>
                                    {
                                        ToggleBuildingWorkbench(player);
                                        OpenShareMenu(player, storedData.SetupPlayer(player.GetUserId()), shareTarget, ShareUiPage.Commands);
                                    });
                            }

                            if (canAdmin)
                            {
                                bool adminOn = PlayerPrivilege.IsAdmin(player);
                                CreateToggleRow(layout, player,
                                    GetString("UI.Commands.Admin", player),
                                    adminOn, ++index,
                                    () =>
                                    {
                                        CmdDcsAdmin(player);
                                        OpenShareMenu(player, playerData, shareTarget, ShareUiPage.Commands);
                                    });
                            }

                            if (canSharePlayer)
                            {
                                CreateInputRow(layout, player,
                                    GetString("UI.Commands.SharePlayer", player),
                                    GetString("UI.Commands.SteamPlaceholder", player),
                                    GetString("UI.Commands.Go", player),
                                    ++index, $"{player.UserIDString}.shareplayer",
                                    value =>
                                    {
                                        CmdSharePlayer(player, new[] { value });
                                    });
                            }
                        });
                });

            ChaosUI.Show(player, root);
        }

        private void CreateMenuHeader(BaseContainer parent, BasePlayer player, StoredData.PlayerData playerData, ulong shareTarget, ShareUiPage page, float width)
        {
            BaseContainer.Create(parent, UIAnchor.TopStretch, new Offset(5, -35, -5, -5))
                .WithChildren(title =>
                {
                    ImageContainer.Create(title, UIAnchor.FullStretch, Offset.zero)
                        .WithStyle(_panelStyle);

                    TextContainer.Create(title, UIAnchor.FullStretch, new Offset(5, 0, -155, 0))
                        .WithText(GetString("UI.Title", player) + (shareTarget == 0UL ? "" : $" <size=8>({shareTarget})</size>"))
                        .WithStyle(_titleStyle);

                    CreateHeaderTab(title, player, GetString("UI.Tab.Sharing", player), -145f, -90f,
                        page == ShareUiPage.Sharing,
                        () => OpenShareMenu(player, playerData, shareTarget, ShareUiPage.Sharing),
                        $"{player.UserIDString}.tab.sharing");

                    CreateHeaderTab(title, player, GetString("UI.Tab.Commands", player), -85f, -30f,
                        page == ShareUiPage.Commands,
                        () => OpenShareMenu(player, playerData, shareTarget, ShareUiPage.Commands),
                        $"{player.UserIDString}.tab.commands");

                    ImageContainer.Create(title, UIAnchor.CenterRight, new Offset(-25, -10, -5, 10))
                        .WithStyle(_buttonStyle)
                        .WithOutline(_outlineRed)
                        .WithChildren(close =>
                        {
                            TextContainer.Create(close, UIAnchor.FullStretch, Offset.zero)
                                .WithText("<b>×</b>")
                                .WithStyle(_closeStyle);

                            ButtonContainer.Create(close, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(_callbackHandler, arg => ChaosUI.Destroy(player, UI_MENU), $"{player.UserIDString}.close");
                        });
                });
        }

        private void CreateHeaderTab(BaseContainer title, BasePlayer player, string label, float xMin, float xMax, bool active, System.Action onClick, string id)
        {
            BaseContainer tab = ImageContainer.Create(title, UIAnchor.CenterRight, new Offset(xMin, -10, xMax, 10))
                .WithStyle(_buttonStyle);

            TextContainer.Create(tab, UIAnchor.FullStretch, Offset.zero)
                .WithText(label)
                .WithStyle(_buttonStyle)
                .WithSize(11);

            ButtonContainer.Create(tab, UIAnchor.FullStretch, Offset.zero)
                .WithColor(Color.Clear)
                .WithCallback(_callbackHandler, arg => onClick(), id);

            if (active)
                tab.WithOutline(_outlineGreen);
        }

        private void CreateInfoRow(BaseContainer layout, BasePlayer player, string langKeyOrText, int index)
        {
            string text = langKeyOrText.StartsWith("UI.") ? GetString(langKeyOrText, player) : langKeyOrText;
            float bottom = -(30f * index);

            BaseContainer.Create(layout, UIAnchor.TopStretch, new Offset(5f, bottom, -5f, bottom + 25f))
                .WithChildren(row =>
                {
                    ImageContainer.Create(row, UIAnchor.FullStretch, Offset.zero)
                        .WithStyle(_panelStyle);

                    TextContainer.Create(row, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
                        .WithText(text)
                        .WithStyle(_labelStyle);
                });
        }

        private void CreateActionRow(BaseContainer layout, BasePlayer player, string label, string buttonText, int index, System.Action onClick)
        {
            float bottom = -(30f * index);

            BaseContainer.Create(layout, UIAnchor.TopStretch, new Offset(5f, bottom, -5f, bottom + 25f))
                .WithChildren(row =>
                {
                    ImageContainer.Create(row, UIAnchor.FullStretch, new Offset(0f, 0f, -72f, 0f))
                        .WithStyle(_panelStyle);

                    TextContainer.Create(row, UIAnchor.FullStretch, new Offset(8f, 0f, -80f, 0f))
                        .WithText(label)
                        .WithStyle(_labelStyle);

                    ImageContainer.Create(row, UIAnchor.CenterRight, new Offset(-70f, -10f, 0f, 10f))
                        .WithStyle(_buttonStyle)
                        .WithChildren(btn =>
                        {
                            TextContainer.Create(btn, UIAnchor.FullStretch, Offset.zero)
                                .WithText(buttonText)
                                .WithStyle(_buttonStyle)
                                .WithSize(12);

                            ButtonContainer.Create(btn, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(_callbackHandler, arg => onClick(), $"{player.UserIDString}.action.{index}");
                        });
                });
        }

        private void CreateToggleRow(BaseContainer layout, BasePlayer player, string label, bool isOn, int index, System.Action onClick)
        {
            float bottom = -(30f * index);

            BaseContainer.Create(layout, UIAnchor.TopStretch, new Offset(5f, bottom, -5f, bottom + 25f))
                .WithChildren(row =>
                {
                    ImageContainer.Create(row, UIAnchor.FullStretch, new Offset(0f, 0f, -27.5f, 0f))
                        .WithStyle(_panelStyle);

                    ImageContainer.Create(row, UIAnchor.FullStretch, new Offset(335f, 0f, 0f, 0f))
                        .WithStyle(_panelStyle);

                    TextContainer.Create(row, UIAnchor.FullStretch, new Offset(8f, 0f, -32f, 0f))
                        .WithText(label)
                        .WithStyle(_labelStyle);

                    ImageContainer.Create(row, UIAnchor.CenterRight, new Offset(-22.5f, -10f, -2.5f, 10f))
                        .WithStyle(_buttonStyle)
                        .WithChildren(toggle =>
                        {
                            if (isOn)
                            {
                                ImageContainer.Create(toggle, UIAnchor.FullStretch, new Offset(2.5f, 2.5f, -2.5f, -2.5f))
                                    .WithColor(_toggleColor)
                                    .WithSprite(Sprites.Background_Rounded)
                                    .WithImageType(Image.Type.Tiled);
                            }

                            ButtonContainer.Create(toggle, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(_callbackHandler, arg => onClick(), $"{player.UserIDString}.toggle.{index}");
                        });
                });
        }

        private void CreateInputRow(BaseContainer layout, BasePlayer player, string label, string placeholder, string buttonText, int index, string callbackId, System.Action<string> onSubmit)
        {
            float bottom = -(30f * index);

            BaseContainer.Create(layout, UIAnchor.TopStretch, new Offset(5f, bottom, -5f, bottom + 25f))
                .WithChildren(row =>
                {
                    ImageContainer.Create(row, UIAnchor.FullStretch, new Offset(0f, 0f, -155f, 0f))
                        .WithStyle(_panelStyle);

                    TextContainer.Create(row, UIAnchor.FullStretch, new Offset(8f, 0f, -160f, 0f))
                        .WithText(label)
                        .WithStyle(_labelStyle);

                    ImageContainer.Create(row, UIAnchor.CenterRight, new Offset(-150f, -10f, -55f, 10f))
                        .WithStyle(_buttonStyle)
                        .WithChildren(inputField =>
                        {
                            InputFieldContainer.Create(inputField, placeholder, UIAnchor.FullStretch, Offset.zero)
                                .WithAlignment(TextAnchor.MiddleCenter)
                                .WithCallback(_callbackHandler, arg =>
                                {
                                    string value = GetUiInput(arg);
                                    if (string.IsNullOrWhiteSpace(value) || value == placeholder)
                                        return;
                                    _uiInputs[callbackId] = value.Trim();
                                }, callbackId);
                        });

                    ImageContainer.Create(row, UIAnchor.CenterRight, new Offset(-50f, -10f, 0f, 10f))
                        .WithStyle(_buttonStyle)
                        .WithChildren(btn =>
                        {
                            TextContainer.Create(btn, UIAnchor.FullStretch, Offset.zero)
                                .WithText(buttonText)
                                .WithStyle(_buttonStyle)
                                .WithSize(11);

                            ButtonContainer.Create(btn, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(_callbackHandler, arg =>
                                {
                                    if (!_uiInputs.TryGetValue(callbackId, out string value) || string.IsNullOrWhiteSpace(value))
                                        return;
                                    onSubmit(value.Trim());
                                }, $"{callbackId}.go");
                        });
                });
        }

        private void CreateShowBlueprintRow(BaseContainer layout, BasePlayer player, string bpCmd, int index)
        {
            float bottom = -(30f * index);

            BaseContainer.Create(layout, UIAnchor.TopStretch, new Offset(5f, bottom, -5f, bottom + 25f))
                .WithChildren(row =>
                {
                    ImageContainer.Create(row, UIAnchor.FullStretch, new Offset(0f, 0f, -102f, 0f))
                        .WithStyle(_panelStyle);

                    TextContainer.Create(row, UIAnchor.FullStretch, new Offset(8f, 0f, -107f, 0f))
                        .WithText(string.Format(GetString("UI.Commands.BsShow", player), bpCmd))
                        .WithStyle(_labelStyle);

                    CreateSmallActionButton(row, player, GetString("UI.Commands.Team", player), -97f, -49f,
                        () => CmdBlueprintShow(player, new[] { "show", "team" }),
                        $"{player.UserIDString}.show.team");

                    CreateSmallActionButton(row, player, GetString("UI.Commands.Clan", player), -44f, 0f,
                        () => CmdBlueprintShow(player, new[] { "show", "clan" }),
                        $"{player.UserIDString}.show.clan");
                });
        }

        private void CreateSmallActionButton(BaseContainer row, BasePlayer player, string label, float xMin, float xMax, System.Action onClick, string id)
        {
            ImageContainer.Create(row, UIAnchor.CenterRight, new Offset(xMin, -10f, xMax, 10f))
                .WithStyle(_buttonStyle)
                .WithChildren(btn =>
                {
                    TextContainer.Create(btn, UIAnchor.FullStretch, Offset.zero)
                        .WithText(label)
                        .WithStyle(_buttonStyle)
                        .WithSize(11);

                    ButtonContainer.Create(btn, UIAnchor.FullStretch, Offset.zero)
                        .WithColor(Color.Clear)
                        .WithCallback(_callbackHandler, arg => onClick(), id);
                });
        }

        private static string GetUiInput(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Args == null || arg.Args.Length < 2)
                return string.Empty;

            if (arg.Args.Length == 2)
                return arg.GetString(1, string.Empty);

            var parts = new string[arg.Args.Length - 1];
            for (int i = 1; i < arg.Args.Length; i++)
                parts[i - 1] = arg.GetString(i, string.Empty);
            return string.Join(" ", parts);
        }

        private bool IsAnyBlueprintSharing(ulong playerId, StoredData.PlayerData data)
        {
            if (data == null) return false;
            if (CanShare(TeamType.Clan, playerId) && data.IsSharing(TeamType.Clan, ShareType.Blueprint))
                return true;
            if (CanShare(TeamType.Friend, playerId) && data.IsSharing(TeamType.Friend, ShareType.Blueprint))
                return true;
            if (CanShare(TeamType.Team, playerId) && data.IsSharing(TeamType.Team, ShareType.Blueprint))
                return true;
            return false;
        }

        private bool HasShareUiAccess(BasePlayer player)
        {
            if (player == null) return false;
            ulong userId = player.GetUserId();
            if (CanShare(TeamType.Clan, userId) || CanShare(TeamType.Friend, userId) || CanShare(TeamType.Team, userId))
                return true;
            if (player.IsAdmin)
                return true;
            if (!string.IsNullOrEmpty(Configuration.Permission.AdminPermission) && player.HasPermission(Configuration.Permission.AdminPermission))
                return true;
            if (HasBlueprintPermission(player, Configuration.Permission.BlueprintUse)
                || HasBlueprintPermission(player, Configuration.Permission.BlueprintToggle)
                || HasBlueprintPermission(player, Configuration.Permission.BlueprintShare)
                || HasBlueprintPermission(player, Configuration.Permission.BlueprintShow))
                return true;
            return false;
        }
    }
}
