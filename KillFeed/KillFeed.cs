using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust.Ai.Gen2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Facepunch;
using UnityEngine;
using Random = Oxide.Core.Random;

namespace Oxide.Plugins
{
    [Info("KillFeed", "codeboy", "2.1.2")]
    public partial class KillFeed : RustPlugin
    {
        #region Classes

        private class SelectOption
        {
            public string Value;
            public string Text;

            public SelectOption(string value, string text)
            {
                Value = value;
                Text = text;
            }
        }

        private class ThemeLabel
        {
            public UITheme Theme;
            public string Text;

            public ThemeLabel(UITheme theme, string text)
            {
                Theme = theme;
                Text = text;
            }
        }

        private class ThemeCard
        {
            public UITheme Theme;
            public string NameKey;
            public string DescKey;

            public ThemeCard(UITheme theme, string nameKey, string descKey)
            {
                Theme = theme;
                NameKey = nameKey;
                DescKey = descKey;
            }
        }

        private class ArrowButton
        {
            public float OffsetX;
            public float OffsetY;
            public string Direction;
            public string Glyph;

            public ArrowButton(float offsetX, float offsetY, string direction, string glyph)
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                Direction = direction;
                Glyph = glyph;
            }
        }

        private class AnchorCell
        {
            public int Column;
            public int Row;
            public AnchorType Type;

            public AnchorCell(int column, int row, AnchorType type)
            {
                Column = column;
                Row = row;
                Type = type;
            }
        }

        internal class ItemEntry
        {
            public string AmmoShortname;
            public string WeaponShortname;
        }

        internal class OffsetSettings
        {
            public float OffsetMinX;
            public float OffsetMaxX;
            public float OffsetMinY;
            public float OffsetMaxY;

            public float Indent;
        }

        internal class AnchorSettings
        {
            public string AnchorMin;
            public string AnchorMax;
        }

        internal class ColoredImage : Image
        {
            [JsonProperty("Color")] public string Color;
        }

        internal class Image
        {
            [JsonProperty("If type = shortname, enter here ItemId")]
            public object Value;

            [JsonConverter(typeof(StringEnumConverter))]
            public ImageType Type;
        }

        private class EntityData
        {
            public string Name;
            public ulong UserID;
        }

        private class WeaponData
        {
            public Image Image = new()
            {
                Value = null,
                Type = ImageType.None
            };

            public int ItemId;
            public ulong SkinID;
            public int AmmoItemId;

            public bool IsItem => ItemId != 0;
        }

        private class AvatarData
        {
            public bool UseSteamId;
            public ulong SteamId;
            public Image Image;
        }

        private class KillData
        {
            public KillData()
            {
                Guid = System.Guid.NewGuid().ToString();
            }

            public string Guid;

            public bool IsNoKiller = false;
            public bool NeedFadeIn = true;

            public EntityData Killer;
            public EntityData Target;

            public float Distance;
            public bool IsHeadshot;
            public WeaponData Weapon;
            public AvatarData KillerAvatar;
            public AvatarData TargetAvatar;
        }

        private class FeedBehaviour : FacepunchBehaviour
        {
            private Dictionary<KillData, int> _kills = new();

            private void Awake()
            {
                InvokeRepeating(CheckExpired, 0, 2f);
            }

            public void AddKill(KillData data)
            {
                _kills.Add(data, (int)Time.realtimeSinceStartup + Instance.cfg.Lifetime);

                if (Instance._exitCoroutine != null)
                {
                    StopCoroutine(Instance._exitCoroutine);
                    Instance._exitCoroutine = null;
                }

                var needsDownload = data.Weapon?.Image is { Type: ImageType.URL };
                if (!needsDownload)
                {
                    UpdateUI(true, data.Guid);
                    return;
                }

                var image = data.Weapon.Image.Value as string;
                if (string.IsNullOrEmpty(image))
                {
                    UpdateUI(true, data.Guid);
                    return;
                }

                Instance.DownloadImage(new()
                {
                    Name = image,
                    Url = image
                }, () => UpdateUI(true, data.Guid));
            }

            private void CheckExpired()
            {
                if (_kills.IsNullOrEmpty())
                    return;

                var toRemove = new List<KillData>();
                var anyVisibleIsExpired = false;
                var expiringVisible = new List<(string guid, int index)>();

                var maxPanels = Instance.cfg.uISettings.MaxPanels;
                var index = 0;
                foreach (var x in _kills.OrderByDescending(x => x.Value))
                {
                    var isVisible = index < maxPanels;

                    if (x.Value - Time.realtimeSinceStartup <= 0)
                    {
                        if (isVisible)
                        {
                            anyVisibleIsExpired = true;
                            expiringVisible.Add((x.Key.Guid, index));
                        }

                        toRemove.Add(x.Key);
                    }

                    index++;
                }

                foreach (var key in toRemove)
                    _kills.Remove(key);

                if (Instance.cfg.uISettings.AnimateFeedExit && !Instance.cfg.PrivateKillsOnly && expiringVisible.Count > 0)
                {
                    Instance.AnimateFeedOut(expiringVisible);
                    return;
                }

                if (anyVisibleIsExpired || (Instance.cfg.PrivateKillsOnly && toRemove.Count > 0))
                    UpdateUI();
            }

            public void PlayerUpdate(BasePlayer player, bool animate = false, string newGuid = null)
            {
                if (Instance._killfeedPreforms.ContainsKey(player))
                {
                    Instance.UI_DrawKills(player, Instance._testkills, true);
                    return;
                }

                if (!Instance.CanSeeKills(player))
                {
                    CuiHelper.DestroyUi(player, LayerFeed);
                    Instance._platePos.Remove(player.userID);
                    return;
                }

                if (!Instance.cfg.PrivateKillsOnly)
                {
                    Instance.UI_DrawKills(player, _kills, false, animate, newGuid);
                    return;
                }

                var visible = new Dictionary<KillData, int>();
                foreach (var pair in _kills)
                    if (pair.Key.Killer.UserID == player.userID || pair.Key.Target.UserID == player.userID)
                        visible.Add(pair.Key, pair.Value);

                if (visible.Count == 0)
                {
                    CuiHelper.DestroyUi(player, LayerFeed);
                    Instance._platePos.Remove(player.userID);
                    return;
                }

                Instance.UI_DrawKills(player, visible, false, animate, newGuid);
            }

            private void UpdateUI(bool animate = false, string newGuid = null)
            {
                foreach (var x in BasePlayer.activePlayerList)
                {
                    PlayerUpdate(x, animate, newGuid);
                }
            }

            public void RefreshFeed()
            {
                UpdateUI();
            }

            public void Destroy()
            {
                foreach (var x in Instance._animCoroutines.Values)
                    if (x != null)
                        StopCoroutine(x);
                Instance._animCoroutines.Clear();

                if (Instance._exitCoroutine != null)
                    StopCoroutine(Instance._exitCoroutine);
                Instance._exitCoroutine = null;

                Instance._platePos.Clear();

                foreach (var x in BasePlayer.activePlayerList)
                    CuiHelper.DestroyUi(x, LayerFeed);
                Destroy(gameObject);
            }
        }

        #endregion

        #region Fields

        [PluginReference] private Plugin ZoneManager;
        [PluginReference] private Plugin MonumentFinder;

        internal enum UITheme : byte
        {
            Rust,
            Midnight,
            Neon
        }

        internal enum ImageLoadingType
        {
            FileManager = 2
        }

        internal enum AnchorType : byte
        {
            RightTop = 0,
            CenterTop = 1,
            LeftTop = 2,
            LeftMiddle = 3,
            Middle = 4,
            RightMiddle = 5,
            LeftBottom = 6,
            CenterBottom = 7,
            RightBottom = 8,
            None = 9
        }

        private readonly Dictionary<KillData, int> _testkills = new();

        private const string PermUse = "killfeed.use";
        private const string PermHideKills = "killfeed.hidekillsfromfeed";
        private const string PermHideDeaths = "killfeed.hidedeathsfromfeed";
        public const string PermAdmin = "killfeed.admin";

        private const string Layer = "ui.KillFeed.admin";
        private const string LayerFeed = "ui.KillFeed";
        private const string FontBold = "robotocondensed-bold.ttf";
        private const string FontRegular = "robotocondensed-regular.ttf";
        public static KillFeed Instance;
        private FeedBehaviour _feedBehaviour;

        private Dictionary<BasePlayer, ConfigData> _killfeedPreforms = new();

        private List<ulong> _disabledFeed = new();

        private Dictionary<ulong, UITheme> _adminThemes = new();

        private readonly Dictionary<ulong, Coroutine> _animCoroutines = new();

        private readonly Dictionary<ulong, Dictionary<string, (float ax, float ay, float bx, float by)>> _platePos = new();

        private Coroutine _exitCoroutine;

        private const float ANIM_INTERVAL = 0.02f;

        private readonly Dictionary<AnchorType, KeyValuePair<string, string>> _anchors = new()
        {
            [AnchorType.Middle] = new("0.5 0.5", "0.5 0.5"),
            [AnchorType.RightTop] = new("1 1", "1 1"),
            [AnchorType.RightMiddle] = new("1 0.5", "1 0.5"),
            [AnchorType.RightBottom] = new("1 0", "1 0"),
            [AnchorType.LeftTop] = new("0 1", "0 1"),
            [AnchorType.LeftMiddle] = new("0 0.5", "0 0.5"),
            [AnchorType.LeftBottom] = new("0 0", "0 0"),
            [AnchorType.CenterTop] = new("0.5 1", "0.5 1"),
            [AnchorType.CenterBottom] = new("0.5 0", "0.5 0"),
            [AnchorType.None] = new("1 1", "1 1")
        };

        private readonly Dictionary<string, ItemEntry> _explosivePrefabToItemShortname = new()
        {
            ["assets/prefabs/misc/chinesenewyear/throwablefirecrackers/firecrackers.deployed.prefab"] = new()
            {
                AmmoShortname = "lunar.firecrackers",
                WeaponShortname = null
            },
            ["assets/prefabs/tools/c4/explosive.timed.deployed.prefab"] = new()
            {
                AmmoShortname = "explosive.timed",
                WeaponShortname = null
            },
            ["assets/prefabs/tools/supply signal/supplysignal.weapon.prefab"] = new()
            {
                AmmoShortname = "supply.signal",
                WeaponShortname = null
            },
            ["assets/prefabs/tools/surveycharge/survey_charge.prefab"] = new()
            {
                AmmoShortname = "surveycharge",
                WeaponShortname = null
            },
            ["assets/prefabs/weapons/beancan grenade/grenade.beancan.deployed.prefab"] = new()
            {
                AmmoShortname = "grenade.beancan",
                WeaponShortname = null
            },
            ["assets/prefabs/weapons/f1 grenade/grenade.f1.deployed.prefab"] = new()
            {
                AmmoShortname = "grenade.f1",
                WeaponShortname = null
            },
            ["assets/prefabs/weapons/flashbang/grenade.flashbang.deployed.prefab"] = new()
            {
                AmmoShortname = "grenade.flashbang",
                WeaponShortname = null
            },
            ["assets/prefabs/weapons/molotov cocktail/grenade.molotov.deployed.prefab"] = new()
            {
                AmmoShortname = "grenade.molotov",
                WeaponShortname = null
            },
            ["assets/prefabs/weapons/satchelcharge/explosive.satchel.deployed.prefab"] = new()
            {
                AmmoShortname = "explosive.satchel",
                WeaponShortname = null
            },
            ["rocket_hv"] = new()
            {
                AmmoShortname = "ammo.rocket.hv",
                WeaponShortname = "rocket.launcher"
            },
            ["rocket_basic"] = new()
            {
                AmmoShortname = "ammo.rocket.basic",
                WeaponShortname = "rocket.launcher"
            },
            ["rocket_fire"] = new()
            {
                AmmoShortname = "ammo.rocket.fire",
                WeaponShortname = "rocket.launcher"
            },
            ["ammo.rifle.explosive"] = new()
            {
                AmmoShortname = "ammo.rifle.explosive",
                WeaponShortname = null
            },
            ["40mm_grenade_he"] = new()
            {
                AmmoShortname = "ammo.grenadelauncher.he",
                WeaponShortname = "multiplegrenadelauncher"
            },
            ["40mm_grenade_buckshot"] = new()
            {
                AmmoShortname = "ammo.grenadelauncher.buckshot",
                WeaponShortname = "multiplegrenadelauncher"
            },
            ["40mm_grenade_smoke"] = new()
            {
                AmmoShortname = "ammo.grenadelauncher.smoke",
                WeaponShortname = "multiplegrenadelauncher"
            }
        };

        internal enum ImageType : byte
        {
            Sprite = 0,
            URL = 1,
            Shortname = 2,
            Text = 3,
            None = 4,
            NotNeed = 5
        }

        internal enum ArrowDirection : byte
        {
            Left = 0,
            Up = 1,
            Right = 2,
            Down = 3
        }

        #endregion

        #region Hooks

        private void OnEntityDeath(PatrolHelicopter patrolHelicopter, HitInfo info)
        {
            if (cfg.AddPatrolHeli && HasPlayerInitiator(info) && patrolHelicopter.lastAttacker is BasePlayer)
                RegisterDeath(patrolHelicopter, info);
        }

        private void OnEntityDeath(BradleyAPC bradley, HitInfo info)
        {
            if (cfg.AddBradley && HasPlayerInitiator(info) && bradley.lastAttacker is BasePlayer)
                RegisterDeath(bradley, info);
        }

        private void OnEntityDeath(BaseNPC2 animal, HitInfo info)
        {
            if (cfg.AddAnimals && HasPlayerInitiator(info) && animal.lastAttacker is BasePlayer && animal.IsAnimal)
                RegisterDeath(animal, info);
        }

        private void OnEntityDeath(BaseAnimalNPC animal, HitInfo info)
        {
            if (cfg.AddAnimals && HasPlayerInitiator(info) && animal.lastAttacker is BasePlayer)
                RegisterDeath(animal, info);
        }

        private void OnEntityDeath(BasePlayer entity, HitInfo info)
        {
            if ((entity.IsBot || entity.IsNpc) && !cfg.AddBots)
                return;

            RegisterDeath(entity, info);
        }

        private void OnEntityDeath(ScientistNPC2 entity, HitInfo info)
        {
            if (cfg.AddBots)
                RegisterDeath(entity, info);
        }

        private bool HasPlayerInitiator(HitInfo info) => info?.InitiatorPlayer != null;

        private void RegisterDeath(BaseCombatEntity target, HitInfo info)
        {
            if (target == null)
                return;

            if (IsDeathHidden(target, info))
                return;

            var isNpcTarget = IsNpc(target);

            if ((IsNpc(info?.InitiatorPlayer) || info?.InitiatorPlayer == null) && isNpcTarget)
                return;

            if (IsKillSuppressed(target, info))
                return;

            var data = new KillData
            {
                Killer = new(),
                Target = new(),
                Weapon = new()
            };

            FillTarget(data, target, isNpcTarget);

            try
            {
                FillKiller(data, target, info);
            }
            catch (Exception e)
            {
                LogRegisterDeathException(e, target, info);
                return;
            }

            data.Weapon = GetWeapon(info, target.lastAttacker, data.Killer.Name);

            FillDistance(data, target, info);

            ValidateKillData(data, out var validatedData);

            if (!validatedData.Target.UserID.IsSteamId() && !validatedData.Killer.UserID.IsSteamId())
                return;

            _feedBehaviour.AddKill(validatedData);
        }

        private bool IsKillSuppressed(BaseCombatEntity target, HitInfo info)
        {
            var killer = info?.InitiatorPlayer;

            if (ZoneManager != null && !string.IsNullOrEmpty(cfg.ZoneManagerFlag))
            {
                if (ZoneHasNoFeedFlag(target))
                    return true;
                if (killer != null && ZoneHasNoFeedFlag(killer))
                    return true;
            }

            if (MonumentFinder != null && cfg.MonumentBlacklist != null && cfg.MonumentBlacklist.Count > 0)
                if (IsInBlacklistedMonument(target.transform.position))
                    return true;

            return false;
        }

        private bool ZoneHasNoFeedFlag(BaseEntity entity)
        {
            if (entity == null || !entity.IsValid())
                return false;

            object result;
            if (entity is BasePlayer player && !player.IsNpc)
                result = ZoneManager.Call("PlayerHasFlag", player, cfg.ZoneManagerFlag);
            else
                result = ZoneManager.Call("EntityHasFlag", entity, cfg.ZoneManagerFlag);

            return result is bool b && b;
        }

        private bool IsInBlacklistedMonument(Vector3 position)
        {
            if (!(MonumentFinder.Call("API_GetClosest", position) is Dictionary<string, object> info))
                return false;

            var name = info.TryGetValue("ShortName", out var sn) ? sn as string : null;
            if (string.IsNullOrEmpty(name) && info.TryGetValue("Alias", out var al))
                name = al as string;
            if (string.IsNullOrEmpty(name))
                return false;

            if (!MonumentMatchesBlacklist(name))
                return false;

            if (info.TryGetValue("IsInBounds", out var fn) && fn is Func<Vector3, bool> inBounds)
                return inBounds(position);

            return true;
        }

        private bool MonumentMatchesBlacklist(string name)
        {
            foreach (var b in cfg.MonumentBlacklist)
                if (!string.IsNullOrEmpty(b) && name.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            return false;
        }

        private bool IsDeathHidden(BaseCombatEntity target, HitInfo info)
        {
            if (target is BasePlayer targetPlayer && targetPlayer.userID.IsSteamId())
                if (permission.UserHasPermission(targetPlayer.UserIDString, PermHideDeaths))
                    return true;

            if (info?.InitiatorPlayer && info?.InitiatorPlayer.userID.IsSteamId() == true)
                if (permission.UserHasPermission(info.InitiatorPlayer.UserIDString, PermHideKills))
                    return true;

            return false;
        }

        private void FillTarget(KillData data, BaseCombatEntity target, bool isNpcTarget)
        {
            if (target is BasePlayer player)
            {
                data.Target.Name = player.displayName;
                data.Target.UserID = player.userID;
            }

            if (isNpcTarget)
                data.Target.Name = target.ShortPrefabName;
        }

        private void FillKiller(KillData data, BaseCombatEntity target, HitInfo info)
        {

            if (info == null)
            {
                FillKillerFromLastAttacker(data, target);
                return;
            }

            data.IsHeadshot = info.isHeadshot;

            if (info.InitiatorPlayer != null)
            {
                FillKillerFromInitiatorPlayer(data, target, info);
                return;
            }

            if (info.Initiator != null)
            {
                data.Killer.Name = info.Initiator.ShortPrefabName;
                return;
            }

            data.IsNoKiller = true;
            data.Killer.Name = "Suicide";
        }

        private void FillKillerFromLastAttacker(KillData data, BaseCombatEntity target)
        {
            if (target.lastAttacker == null)
            {
                data.IsNoKiller = true;
                data.Killer.Name = target.lastDamage != null ? target.lastDamage.ToString() : "Suicide";
                return;
            }

            var attacker = target.lastAttacker;

            if (attacker is BasePlayer playerAttacker)
            {
                if (IsNpc(playerAttacker))
                {
                    data.Killer.Name = playerAttacker.ShortPrefabName;
                }
                else
                {
                    data.Killer.Name = playerAttacker.displayName;
                    data.Killer.UserID = playerAttacker.userID;
                }
            }
            else
                data.Killer.Name = attacker.ShortPrefabName;
        }

        private void FillKillerFromInitiatorPlayer(KillData data, BaseCombatEntity target, HitInfo info)
        {
            if (IsNpc(info.InitiatorPlayer))
            {
                data.Killer.Name = info.InitiatorPlayer.ShortPrefabName;
                return;
            }

            if (target == info.InitiatorPlayer)
            {
                data.Killer.Name = "Suicide";
                data.IsNoKiller = true;
                return;
            }

            data.Killer.Name = info.InitiatorPlayer.displayName;
            data.Killer.UserID = info.InitiatorPlayer.userID;
            data.Weapon.Image.Type = ImageType.NotNeed;
        }

        private void FillDistance(KillData data, BaseCombatEntity target, HitInfo info)
        {
            if (info == null || data.Distance > 0.1f)
                return;

            var initiator = info.Initiator;
            var attacker = target.lastAttacker;

            if (!initiator && !attacker)
                return;

            var sourcePos = initiator == null ? attacker.transform.position : initiator.transform.position;
            data.Distance = (float)Math.Round(Vector3.Distance(target.transform.position, sourcePos), 1);
        }

        private void LogRegisterDeathException(Exception e, BaseCombatEntity target, HitInfo info)
        {
            PrintError(
                $"Error is caused in RegisterDeath method. Exception logged to file exceptions. Please, contact with developer. Exception - {e.Message}");
            try
            {
                LogToFile("exceptions",
                    $"Exception caused. Info - {info != null}, target.lastDamage - {target?.lastDamage}, initiator {info?.Initiator != null}, initiatorPlayer {info?.InitiatorPlayer != null}",
                    this, true, true);
            }
            catch
            {
            }
        }

        private void OnServerInitialized()
        {
            Instance = this;

            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermHideKills, this);
            permission.RegisterPermission(PermHideDeaths, this);

            LoadData();

            RevalidateStoredImages();

            _feedBehaviour = new GameObject().AddComponent<FeedBehaviour>();

            timer.Once(2f, () =>
            {

                LoadRequiredImages();

                SeedTestKills();
                PreloadImages();
            });
        }

        private void SeedTestKills()
        {
            string[] deathReasons = { "Fall", "Drowned", "Suicide", "Bleeding" };

            for (var i = 0; i < 30; i++)
            {
                var isNoKiller = i % 3 == 1;

                if (isNoKiller)
                {
                    var reason = deathReasons[i % deathReasons.Length];
                    _testkills.Add(new()
                    {
                        Guid = $"GUID{i}",
                        NeedFadeIn = i == 0,
                        IsNoKiller = true,
                        Killer = new()
                        {
                            Name = reason,
                            UserID = 0
                        },
                        Target = new()
                        {
                            Name = $"TARGET{i}",
                            UserID = (ulong)(i + 1)
                        },
                        Distance = 0,
                        IsHeadshot = false,
                        Weapon = new()
                        {
                            Image = GetImageForEntity(reason),
                            ItemId = 0,
                            SkinID = 0,
                            AmmoItemId = 0
                        },
                    }, int.MaxValue);
                    continue;
                }

                _testkills.Add(new()
                {
                    Guid = $"GUID{i}",
                    NeedFadeIn = i == 0,
                    Killer = new()
                    {
                        Name = $"KILLER{i}",
                        UserID = (ulong)i
                    },
                    Target = new()
                    {
                        Name = $"TARGET{i}",
                        UserID = (ulong)(i + 1)
                    },
                    Distance = Random.Range(0, 100),
                    IsHeadshot = true,
                    Weapon = new()
                    {
                        Image = null,
                        ItemId = 1545779598,
                        SkinID = 0,
                        AmmoItemId = -1321651331
                    },
                    KillerAvatar = new()
                    {
                        UseSteamId = true,
                        SteamId = 76561197960265728UL + (ulong)i
                    },
                    TargetAvatar = new()
                    {
                        UseSteamId = true,
                        SteamId = 76561197960265728UL + (ulong)(i + 1)
                    },
                }, int.MaxValue);
            }
        }

        private void PreloadImages()
        {
            if (cfg.uISettings.HeadshotImage.Type == ImageType.URL)
            {
                var image = cfg.uISettings.HeadshotImage.Value as string;
                DownloadImage(new()
                {
                    Name = image,
                    Url = image
                }, null);
            }

            foreach (var x in cfg.EntityToImage)
            {
                var type = GetImageType(x.Value);

                if (type == ImageType.Sprite || IsRequiredImage(x.Value))
                    continue;

                DownloadImage(new() { Name = x.Value, Url = x.Value }, null);
            }
        }

        private bool IsRequiredImage(string name)
        {
            var key = name.EndsWith(".png") ? name.Substring(0, name.Length - 4) : name;
            foreach (var required in RequiredImages)
            {
                var requiredKey = required.EndsWith(".png") ? required.Substring(0, required.Length - 4) : required;
                if (string.Equals(key, requiredKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void Unload()
        {
            SaveData();

            foreach (var x in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(x, Layer);
                CuiHelper.DestroyUi(x, LayerFeed);
            }

            if (_feedBehaviour != null)
                _feedBehaviour.Destroy();

            Instance = null;
        }

        private void OnServerSave() => SaveData();

        #endregion

        #region Methods

        private WeaponData GetWeapon(HitInfo info, BaseEntity lastAttacker = null, string killerName = "")
        {
            var weaponData = new WeaponData();
            try
            {
                if (info?.Weapon != null)
                    FillWeaponFromHeldWeapon(weaponData, info.Weapon);
                else if (info?.WeaponPrefab != null)
                    FillWeaponFromPrefab(weaponData, info.WeaponPrefab);
                else if (lastAttacker is BasePlayer attacker)
                    FillWeaponFromActiveItem(weaponData, attacker);

                if (weaponData.Image is { Type: ImageType.None })
                    weaponData.Image = GetImageForEntity(killerName);
            }
            catch (Exception ex)
            {
                PrintWarning($"[GetWeapon] Error: {ex.Message}");
                if (weaponData.Image == null || weaponData.Image.Type == ImageType.None)
                    weaponData.Image = GetImageForEntity(killerName);
            }

            return weaponData;
        }

        private void FillWeaponFromHeldWeapon(WeaponData weaponData, AttackEntity weapon)
        {
            var cachedWeapon = weapon.GetCachedItem();
            if (cachedWeapon?.info == null)
                return;

            weaponData.ItemId = cachedWeapon.info.itemid;
            weaponData.SkinID = cachedWeapon.skin;
            weaponData.AmmoItemId = cachedWeapon.GetHeldEntity()?.GetComponent<BaseProjectile>()
                ?.primaryMagazine?.ammoType?.itemid ?? 0;
        }

        private void FillWeaponFromPrefab(WeaponData weaponData, BaseEntity weaponPrefab)
        {
            if (!_explosivePrefabToItemShortname.TryGetValue(weaponPrefab.ShortPrefabName, out var entry) &&
                !_explosivePrefabToItemShortname.TryGetValue(weaponPrefab.PrefabName, out entry))
                return;

            var ammoDef = ItemManager.FindItemDefinition(entry.AmmoShortname);

            if (!string.IsNullOrEmpty(entry.WeaponShortname))
            {
                var weaponDef = ItemManager.FindItemDefinition(entry.WeaponShortname);
                if (weaponDef != null)
                    weaponData.ItemId = weaponDef.itemid;
            }

            if (ammoDef != null)
                weaponData.AmmoItemId = ammoDef.itemid;
            weaponData.Image.Type = ImageType.NotNeed;
        }

        private void FillWeaponFromActiveItem(WeaponData weaponData, BasePlayer attacker)
        {
            var item = attacker.GetActiveItem();
            if (item == null)
                return;

            weaponData.ItemId = item.info.itemid;
            weaponData.SkinID = item.skin;
            weaponData.AmmoItemId = item.GetHeldEntity()?.GetComponent<BaseProjectile>()
                ?.primaryMagazine?.ammoType?.itemid ?? 0;
        }

        private void ValidateKillData(KillData data, out KillData validatedData)
        {
            var weaponImage = data.Weapon?.Image ?? new() { Value = null, Type = ImageType.None };

            validatedData = new()
            {
                IsNoKiller = data.IsNoKiller,
                Killer = new()
                {
                    Name = data.Killer?.Name ?? "Unknown",
                    UserID = data.Killer?.UserID ?? 0
                },
                Target = new()
                {
                    Name = data.Target?.Name ?? "Unknown",
                    UserID = data.Target?.UserID ?? 0
                },
                Distance = data.Distance,
                IsHeadshot = data.IsHeadshot,
                Weapon = new()
                {
                    Image = weaponImage,
                    ItemId = data.Weapon?.ItemId ?? 0,
                    SkinID = data.Weapon?.SkinID ?? 0,
                    AmmoItemId = data.Weapon?.AmmoItemId ?? 0
                }
            };

            if (string.IsNullOrEmpty(validatedData.Killer.Name))
                validatedData.Killer.Name = "Suicide";

            if (string.IsNullOrEmpty(validatedData.Target.Name))
                validatedData.Target.Name = "Unknown";

            validatedData.KillerAvatar = BuildAvatar(validatedData.Killer);
            validatedData.TargetAvatar = BuildAvatar(validatedData.Target);

            if (validatedData.Weapon.Image != null && validatedData.Weapon.Image.Type == ImageType.Shortname &&
                validatedData.Weapon.Image.Value != null)
            {
                var def = ItemManager.FindItemDefinition(validatedData.Weapon.Image.Value.ToString());
                if (def != null)
                    validatedData.Weapon.Image.Value = def.itemid;
                else
                    validatedData.Weapon.Image = cfg.DefaultDeathImage;
            }
        }

        private bool IsNpc(BaseCombatEntity entity)
        {
            if (entity == null)
                return false;

            if (entity is BasePlayer player)
                return player.IsNpc || player.IsBot || !player.userID.IsSteamId();

            if (entity is BradleyAPC or PatrolHelicopter or CH47Helicopter)
                return true;

            return entity.IsNpc;
        }

        private int ValidateItemID(object key)
        {
            if (key == null)
                return 0;

            ItemDefinition def = null;
            try
            {
                if (key is int intKey)
                    def = ItemManager.FindItemDefinition(intKey);
                else if (key is long longKey)
                    def = ItemManager.FindItemDefinition((int)longKey);
                else if (key is string strKey)
                {
                    if (int.TryParse(strKey, out var parsedId))
                        def = ItemManager.FindItemDefinition(parsedId);
                    else
                        def = ItemManager.FindItemDefinition(strKey);
                }
                else
                {
                    var str = key.ToString();
                    if (int.TryParse(str, out var parsedId2))
                        def = ItemManager.FindItemDefinition(parsedId2);
                    else
                        def = ItemManager.FindItemDefinition(str);
                }
            }
            catch
            {
                return 0;
            }

            return def?.itemid ?? 0;
        }

        private ImageType GetImageType(string image)
        {
            if (string.IsNullOrEmpty(image))
                return ImageType.None;

            if (image.Contains("http") || (image.Contains("png") && !image.Contains("assets/")))
                return ImageType.URL;
            if (image.Contains("assets/"))
                return ImageType.Sprite;
            return ImageType.Shortname;
        }

        private bool CanSeeKills(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse))
                return false;
            if (_disabledFeed.Contains(player.userID))
                return false;

            return true;
        }

        private string GetImage(string image)
        {
            if (string.IsNullOrEmpty(image))
                return "";

            try
            {
                return ImageStore.Get(image);
            }
            catch (Exception ex)
            {
                PrintWarning($"[ImageSystem] Error getting image '{image}': {ex.Message}");
                return "";
            }
        }

        #region Image storage

        private const string ImagesFolder = "Images";

        private static readonly string[] RequiredImages =
        {
            "kf_round_group.png",
            "kf_round_button.png"
        };

        private Dictionary<string, string> _imageList = new();

        private class ImageSettings
        {
            [JsonProperty(PropertyName = "Name", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string Name;

            [JsonProperty(PropertyName = "Path", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string Url;
        }

        private static class ImageStore
        {
            private static readonly List<string> _reportedMissing = new();

            public static string Get(string key)
            {
                if (Instance._imageList.TryGetValue(key, out var id) ||
                    Instance._imageList.TryGetValue(StripPng(key), out id) ||
                    Instance._imageList.TryGetValue(key + ".png", out id))
                    return id;

                return "";
            }

            public static void LoadFromFolder(List<string> fileNames, Action callback)
            {
                if (fileNames == null || fileNames.Count == 0)
                {
                    callback?.Invoke();
                    return;
                }

                ServerMgr.Instance.StartCoroutine(LoadRoutine(fileNames, callback));
            }

            private static IEnumerator LoadRoutine(List<string> fileNames, Action callback)
            {
                foreach (var rawName in fileNames)
                {
                    if (string.IsNullOrEmpty(rawName))
                        continue;

                    var key = StripPng(rawName);
                    var fileName = key + ".png";
                    var fullPath = Path.Combine(Interface.Oxide.DataDirectory, "KillFeed", ImagesFolder, fileName);
                    if (!File.Exists(fullPath))
                    {
                        var hi = Path.Combine(Oxide.Core.OxideMod.ResolveServerRoot(), "HarmonyImages", "KillFeed", fileName);
                        if (File.Exists(hi)) fullPath = hi;
                    }
                    fullPath = fullPath
                        .Replace('\\', '/');

                    if (!File.Exists(fullPath))
                    {
                        ReportMissing(fileName, "File not found");
                        yield return CoroutineEx.waitForFixedUpdate;
                        continue;
                    }

                    StoreFromFile(key, fullPath);
                    yield return CoroutineEx.waitForFixedUpdate;
                }

                callback?.Invoke();
            }

            private static void StoreFromFile(string key, string fullPath)
            {
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(fullPath);
                }
                catch (Exception ex)
                {
                    ReportMissing(key, ex.Message);
                    return;
                }

                var texture = new Texture2D(2, 2);
                try
                {
                    if (!texture.LoadImage(bytes))
                    {
                        ReportMissing(key, "Failed to decode image");
                        return;
                    }

                    var imageId = FileStorage.server.Store(texture.EncodeToPNG(), FileStorage.Type.png,
                        CommunityEntity.ServerInstance.net.ID);

                    Instance._imageList[key] = imageId.ToString();
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            private static void ReportMissing(string imageName, string err)
            {
                if (_reportedMissing.Contains(imageName))
                    return;
                _reportedMissing.Add(imageName);

                var folder = Path.Combine(Interface.Oxide.DataDirectory, "KillFeed", ImagesFolder).Replace('\\', '/');
                Debug.LogError(
                    $"[KillFeed] Image '{imageName}' was not loaded. Upload it to the {folder} folder.\nError - {err}");
            }

            private static string StripPng(string name) =>
                name.EndsWith(".png") ? name.Substring(0, name.Length - 4) : name;
        }

        private void LoadRequiredImages()
        {
            ImageStore.LoadFromFolder(new List<string>(RequiredImages), WarnIfImagesMissing);
        }

        private List<string> GetMissingImageFiles()
        {
            var missing = new List<string>();
            var folder = Path.Combine(Interface.Oxide.DataDirectory, "KillFeed", ImagesFolder).Replace('\\', '/');

            foreach (var image in RequiredImages)
            {
                var fullPath = Path.Combine(folder, image).Replace('\\', '/');
                if (!File.Exists(fullPath))
                    missing.Add(image);
            }

            return missing;
        }

        private void WarnIfImagesMissing()
        {
            var missing = GetMissingImageFiles();
            if (missing.Count == 0)
                return;

            var folder = Path.Combine(Interface.Oxide.DataDirectory, "KillFeed", ImagesFolder).Replace('\\', '/');
            Debug.LogWarning(
                $"[KillFeed] Missing {missing.Count} required image(s): {string.Join(", ", missing)}. " +
                $"Upload them to the {folder} folder.");
        }

        private void DownloadImage(ImageSettings settings, Action callback)
        {
            if (settings == null || string.IsNullOrEmpty(settings.Name))
            {
                callback?.Invoke();
                return;
            }

            try
            {
                ImageStore.LoadFromFolder(new() { settings.Name }, callback);
            }
            catch (Exception ex)
            {
                PrintWarning($"[ImageSystem] Error downloading image '{settings.Name}': {ex.Message}");
                callback?.Invoke();
            }
        }

        #endregion

        private Image DefaultDeathImageOrSkull =>
            cfg.DefaultDeathImage ?? new() { Value = "assets/icons/skull.png", Type = ImageType.Sprite };

        private AvatarData BuildAvatar(EntityData entity)
        {
            if (entity == null)
                return null;

            if (entity.UserID.IsSteamId())
                return new AvatarData
                {
                    UseSteamId = true,
                    SteamId = entity.UserID
                };

            return new AvatarData
            {
                UseSteamId = false,
                Image = GetImageForEntity(entity.Name)
            };
        }

        private string ResolveEntityImageValue(string entity)
        {
            if (string.IsNullOrEmpty(entity))
                return null;

            if (cfg.EntityToImage.TryGetValue(entity, out var exact) && !string.IsNullOrEmpty(exact))
                return exact;

            string bestKey = null;
            string bestImage = null;
            foreach (var pair in cfg.EntityToImage)
            {
                if (string.IsNullOrEmpty(pair.Value) || pair.Value.StartsWith("assets/"))
                    continue;
                if (entity.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (bestKey == null || pair.Key.Length > bestKey.Length)
                {
                    bestKey = pair.Key;
                    bestImage = pair.Value;
                }
            }

            return bestImage;
        }

        private Image GetImageForEntity(string entity)
        {
            var image = ResolveEntityImageValue(entity);
            if (string.IsNullOrEmpty(image))
                return DefaultDeathImageOrSkull;

            var img = new Image();

            var def = ItemManager.FindItemDefinition(image);
            if (def != null)
            {
                img.Type = ImageType.Shortname;
                img.Value = def.itemid;
                return img;
            }

            img.Value = image;

            if (image.Contains("assets/"))
            {
                img.Type = ImageType.Sprite;
                return img;
            }

            if (image.Contains("http"))
            {
                img.Type = ImageType.URL;
                return img;
            }

            var cachedImage = GetImage(image);
            if (string.IsNullOrEmpty(cachedImage))
                cachedImage = GetImage(entity);

            if (!string.IsNullOrEmpty(cachedImage))
            {
                img.Value = image;
                img.Type = ImageType.URL;
                return img;
            }

            return DefaultDeathImageOrSkull;
        }

        private string GetIdealNameForFeed(string name, string language = "en")
        {
            if (string.IsNullOrEmpty(name))
                return "UNKNOWN";

            var findedName = cfg.EntityToName.FirstOrDefault(x => name.Contains(x.Key, CompareOptions.IgnoreCase));
            if (findedName.Value == null)
                return name;

            if (!findedName.Value.TryGetValue(language, out var text))
                text = findedName.Value["en"];

            return text;
        }

        private string ConcatArgs(string[] args, int startIndex = 0)
        {
            if (args == null || startIndex >= args.Length)
                return string.Empty;

            return string.Join(" ", args, startIndex, args.Length - startIndex);
        }

        #endregion

        #region UI Core

        private class Theme
        {
            public string Bg;
            public string Bg2;
            public string Surface;
            public string Elev;
            public string Border;
            public string Text;
            public string Text2;
            public string Accent;
            public string AccentH;
            public string AccentText;
        }

        private readonly Dictionary<UITheme, Theme> _themes = new()
        {
            [UITheme.Rust] = new()
            {
                Bg = "0.118 0.106 0.094 1",
                Bg2 = "0.082 0.067 0.059 1",
                Surface = "0.165 0.149 0.133 1",
                Elev = "0.212 0.188 0.161 1",
                Border = "0.282 0.251 0.227 1",
                Text = "0.929 0.894 0.839 1",
                Text2 = "0.659 0.620 0.564 1",
                Accent = "0.816 0.541 0.376 1",
                AccentH = "0.878 0.627 0.467 1",
                AccentText = "0.102 0.071 0.047 1"
            },
            [UITheme.Midnight] = new()
            {
                Bg = "0.059 0.086 0.125 1",
                Bg2 = "0.039 0.059 0.090 1",
                Surface = "0.086 0.125 0.180 1",
                Elev = "0.118 0.169 0.239 1",
                Border = "0.165 0.227 0.322 1",
                Text = "0.902 0.933 0.973 1",
                Text2 = "0.561 0.627 0.722 1",
                Accent = "0.357 0.776 0.902 1",
                AccentH = "0.498 0.847 0.949 1",
                AccentText = "0.039 0.059 0.090 1"
            },
            [UITheme.Neon] = new()
            {
                Bg = "0.071 0.047 0.102 1",
                Bg2 = "0.039 0.024 0.075 1",
                Surface = "0.106 0.071 0.157 1",
                Elev = "0.141 0.094 0.212 1",
                Border = "0.227 0.149 0.333 1",
                Text = "0.941 0.906 0.980 1",
                Text2 = "0.655 0.584 0.741 1",
                Accent = "0.690 0.380 1 1",
                AccentH = "0.788 0.533 1 1",
                AccentText = "0.071 0.047 0.102 1"
            }
        };

        private Theme GetTheme(BasePlayer player)
        {
            if (_killfeedPreforms.TryGetValue(player, out var preform) &&
                _themes.TryGetValue(preform.EditorTheme, out var theme))
                return theme;

            return _themes[UITheme.Rust];
        }

        private ConfigData MakePreform(BasePlayer player)
        {
            var clone = cfg.Clone();
            clone.EditorTheme = _adminThemes.TryGetValue(player.userID, out var th) ? th : UITheme.Rust;
            return clone;
        }

        private class NavCategory
        {
            public string TitleKey;
            public List<NavItem> Items;
        }

        private class NavItem
        {
            public string Icon;
            public string LabelKey;
            public string Id;
        }

        private readonly List<NavCategory> _nav = new()
        {
            new()
            {
                TitleKey = "nav.cat.base",
                Items = new()
                {
                    new() { Icon = "⚙", LabelKey = "nav.general", Id = "general" },
                    new() { Icon = "▦", LabelKey = "nav.layout", Id = "layout" }
                }
            },
            new()
            {
                TitleKey = "nav.cat.text",
                Items = new()
                {
                    new() { Icon = "⚔", LabelKey = "nav.killer", Id = "killer" },
                    new() { Icon = "☠", LabelKey = "nav.target", Id = "target" },
                    new() { Icon = "↔", LabelKey = "nav.distance", Id = "distance" }
                }
            },
            new()
            {
                TitleKey = "nav.cat.graphics",
                Items = new()
                {
                    new() { Icon = "▤", LabelKey = "nav.icons", Id = "icons" },
                    new() { Icon = "✦", LabelKey = "nav.anim", Id = "anim" }
                }
            },
            new()
            {
                TitleKey = "nav.cat.extra",
                Items = new()
                {
                    new() { Icon = "▣", LabelKey = "nav.elements", Id = "elements" },
                    new() { Icon = "⊘", LabelKey = "nav.filters", Id = "filters" },
                    new() { Icon = "◐", LabelKey = "nav.themes", Id = "themes" }
                }
            }
        };
        private void UI_GetOutline(ref CuiElementContainer container, string parent, string color, float width, bool up,
            bool down, bool left, bool right)
        {
            if (up)
                container.Add(new CuiElement()
                {
                    Parent = parent,
                    Components =
                    {
                        new CuiImageComponent() { Color = color },
                        new CuiRectTransformComponent()
                            { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMax = $"0 {width}" }
                    }
                });
            if (down)
                container.Add(new CuiElement()
                {
                    Parent = parent,
                    Components =
                    {
                        new CuiImageComponent() { Color = color },
                        new CuiRectTransformComponent()
                            { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = $"0 {-width}", OffsetMax = $"0 0" }
                    }
                });
            if (left)
                container.Add(new CuiElement()
                {
                    Parent = parent,
                    Components =
                    {
                        new CuiImageComponent() { Color = color },
                        new CuiRectTransformComponent()
                            { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = $"{-width} 0" }
                    }
                });
            if (right)
                container.Add(new CuiElement()
                {
                    Parent = parent,
                    Components =
                    {
                        new CuiImageComponent() { Color = color },
                        new CuiRectTransformComponent()
                            { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMax = $"{width} 0" }
                    }
                });
        }

        private const float PanelW = 760f;
        private const float PanelH = 560f;
        private const float HeaderH = 48f;
        private const float FooterH = 52f;
        private const float SidebarW = 190f;

        private void UI_DrawMain(BasePlayer player)
        {
            if (!_killfeedPreforms.ContainsKey(player))
            {
                _killfeedPreforms.Remove(player);
                _killfeedPreforms.Add(player, MakePreform(player));
            }

            var t = GetTheme(player);
            var container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = Layer,
                Parent = "Overlay",
                DestroyUi = Layer,
                Components =
                {
                    new CuiImageComponent { Color = t.Surface },
                    new CuiOutlineComponent { Color = t.Border, Distance = "1 1" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                        OffsetMin = $"{-PanelW / 2f} {-PanelH / 2f}", OffsetMax = $"{PanelW / 2f} {PanelH / 2f}"
                    },
                    new CuiNeedsCursorComponent(),
                }
            });

            UI_DrawHeader(ref container, t);
            UI_DrawNav(ref container, t, GetActiveSection(player), player);
            UI_DrawFooter(ref container, t, player);

            CuiHelper.AddUi(player, container);

            UI_DrawSectionContent(player, GetActiveSection(player));
        }

        private void UI_DrawHeader(ref CuiElementContainer container, Theme t)
        {
            container.Add(new CuiElement
            {
                Name = Layer + ".header",
                Parent = Layer,
                Components =
                {
                    new CuiImageComponent { Color = t.Bg2 },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = $"0 {-HeaderH}", OffsetMax = "0 0"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = Layer + ".header",
                Components =
                {
                    new CuiImageComponent { Color = t.Border },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 -1", OffsetMax = "0 0" }
                }
            });

            container.Add(new CuiElement
            {
                Name = Layer + ".header.logo",
                Parent = Layer + ".header",
                Components =
                {
                    new CuiRawImageComponent { Color = t.Accent, Png = GetImage("kf_round_button.png") },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "14 -12", OffsetMax = "38 12" }
                }
            });

            container.Add(new CuiElement
            {
                Parent = Layer + ".header.logo",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "K", Font = FontBold, FontSize = 13,
                        Align = TextAnchor.MiddleCenter, Color = t.AccentText
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });

            container.Add(new CuiElement
            {
                Parent = Layer + ".header",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "KillFeed", Font = FontBold, FontSize = 15,
                        Align = TextAnchor.MiddleLeft, Color = t.Text
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "46 -14", OffsetMax = "160 14" }
                }
            });

            UI_DrawThemeSwitch(ref container, t);

            container.Add(new CuiButton
            {
                Button = { Command = "kf.cancelpreform", Color = t.Elev },
                Text =
                {
                    Text = "✕", Font = FontBold, FontSize = 13,
                    Align = TextAnchor.MiddleCenter, Color = t.Text2
                },
                RectTransform =
                    { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-42 -14", OffsetMax = "-14 14" }
            }, Layer + ".header", Layer + ".header.close");
        }

        private void UI_DrawThemeSwitch(ref CuiElementContainer container, Theme active)
        {
            container.Add(new CuiElement
            {
                Name = Layer + ".header.themes",
                Parent = Layer + ".header",
                Components =
                {
                    new CuiRawImageComponent { Color = active.Elev, Png = GetImage("kf_round_button.png") },
                    new CuiRectTransformComponent
                        { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-238 -13", OffsetMax = "-50 13" }
                }
            });

            var labels = new[]
            {
                new ThemeLabel(UITheme.Rust, "RUST"),
                new ThemeLabel(UITheme.Midnight, "MIDNIGHT"),
                new ThemeLabel(UITheme.Neon, "NEON")
            };

            for (var i = 0; i < labels.Length; i++)
            {
                var isActive = _themes[labels[i].Theme] == active;
                var btnMin = $"{2f + i * (188f / labels.Length)} 2";
                var btnMax = $"{2f + i * (188f / labels.Length) + (188f / labels.Length) - 2f} -2";

                if (isActive)
                    container.Add(new CuiElement
                    {
                        Name = Layer + ".header.themes.bg." + labels[i].Theme,
                        Parent = Layer + ".header.themes",
                        Components =
                        {
                            new CuiRawImageComponent { Color = active.Accent, Png = GetImage("kf_round_button.png") },
                            new CuiRectTransformComponent
                                { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = btnMin, OffsetMax = btnMax }
                        }
                    });

                container.Add(new CuiButton
                {
                    Button = { Command = $"kf.theme {labels[i].Theme}", Color = "0 0 0 0" },
                    Text =
                    {
                        Text = labels[i].Text, Font = FontBold, FontSize = 10,
                        Align = TextAnchor.MiddleCenter, Color = isActive ? active.AccentText : active.Text2
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 0", AnchorMax = "0 1",
                        OffsetMin = btnMin, OffsetMax = btnMax
                    }
                }, Layer + ".header.themes", Layer + ".header.themes.btn." + labels[i].Theme);
            }
        }

        private void UI_DrawNav(ref CuiElementContainer container, Theme t, string activeId, BasePlayer player)
        {
            container.Add(new CuiElement
            {
                Name = Layer + ".nav",
                Parent = Layer,
                DestroyUi = Layer + ".nav",
                Components =
                {
                    new CuiImageComponent { Color = t.Bg },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "0 1",
                        OffsetMin = $"0 {FooterH}", OffsetMax = $"{SidebarW} {-HeaderH}"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = Layer + ".nav",
                Components =
                {
                    new CuiImageComponent { Color = t.Border },
                    new CuiRectTransformComponent
                        { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "1 0" }
                }
            });

            var navScroll = Layer + ".nav.scroll";
            var navContentH = 14f;
            foreach (var cat in _nav)
                navContentH += 30f + cat.Items.Count * 34f + 6f;
            navContentH += 8f;

            container.Add(new CuiElement
            {
                Name = navScroll,
                Parent = Layer + ".nav",
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiScrollViewComponent
                    {
                        Horizontal = false,
                        Vertical = true,
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Clamped,
                        Inertia = true,
                        ScrollSensitivity = 24f,
                        ContentTransform = new CuiRectTransform
                        {
                            AnchorMin = "0 1", AnchorMax = "1 1",
                            OffsetMin = $"0 {-navContentH}", OffsetMax = "0 0"
                        },
                        VerticalScrollbar = ThemedScrollbar(t)
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 10", OffsetMax = "-8 -10" }
                }
            });

            var cursorY = -14f;

            foreach (var cat in _nav)
            {
                container.Add(new CuiElement
                {
                    Parent = navScroll,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = GetLocalizedMessage(cat.TitleKey, player), Font = FontBold, FontSize = 9,
                            Align = TextAnchor.LowerLeft, Color = t.Text2
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1", AnchorMax = "1 1",
                            OffsetMin = $"16 {cursorY - 22f}", OffsetMax = $"-8 {cursorY}"
                        }
                    }
                });
                cursorY -= 30f;

                foreach (var item in cat.Items)
                {
                    container.Add(new CuiButton
                    {
                        Button = { Command = $"kf.section {item.Id}", Color = item.Id == activeId ? t.Elev : "0 0 0 0" },
                        Text = { Text = "" },
                        RectTransform =
                        {
                            AnchorMin = "0 1", AnchorMax = "1 1",
                            OffsetMin = $"8 {cursorY - 32f}", OffsetMax = $"-8 {cursorY}"
                        }
                    }, navScroll, Layer + ".nav.item." + item.Id);

                    if (item.Id == activeId)
                        container.Add(new CuiElement
                        {
                            Parent = Layer + ".nav.item." + item.Id,
                            Components =
                            {
                                new CuiImageComponent { Color = t.Accent },
                                new CuiRectTransformComponent
                                    { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0 -9", OffsetMax = "3 9" }
                            }
                        });

                    container.Add(new CuiElement
                    {
                        Parent = Layer + ".nav.item." + item.Id,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = item.Icon, Font = FontRegular, FontSize = 13,
                                Align = TextAnchor.MiddleCenter, Color = item.Id == activeId ? t.Accent : t.Text2
                            },
                            new CuiRectTransformComponent
                                { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "10 0", OffsetMax = "30 0" }
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = Layer + ".nav.item." + item.Id,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = GetLocalizedMessage(item.LabelKey, player), Font = FontRegular, FontSize = 12,
                                Align = TextAnchor.MiddleLeft, Color = item.Id == activeId ? t.Accent : t.Text
                            },
                            new CuiRectTransformComponent
                                { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "34 0", OffsetMax = "0 0" }
                        }
                    });

                    cursorY -= 34f;
                }

                cursorY -= 6f;
            }
        }

        private void UI_DrawFooter(ref CuiElementContainer container, Theme t, BasePlayer player)
        {
            container.Add(new CuiElement
            {
                Name = Layer + ".footer",
                Parent = Layer,
                Components =
                {
                    new CuiImageComponent { Color = t.Bg2 },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 0",
                        OffsetMin = "0 0", OffsetMax = $"0 {FooterH}"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = Layer + ".footer",
                Components =
                {
                    new CuiImageComponent { Color = t.Border },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 1" }
                }
            });

            container.Add(new CuiElement
            {
                Name = Layer + ".footer.reset",
                Parent = Layer + ".footer",
                Components =
                {
                    new CuiRawImageComponent { Color = t.Elev, Png = GetImage("kf_round_button.png") },
                    new CuiRectTransformComponent
                        { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-270 -15", OffsetMax = "-150 15" }
                }
            });
            container.Add(new CuiButton
            {
                Button = { Command = "kf.reset", Color = "0 0 0 0" },
                Text =
                {
                    Text = GetLocalizedMessage("btn.reset", player), Font = FontBold, FontSize = 12,
                    Align = TextAnchor.MiddleCenter, Color = t.Text2
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, Layer + ".footer.reset", Layer + ".footer.reset.btn");

            container.Add(new CuiElement
            {
                Name = Layer + ".footer.save",
                Parent = Layer + ".footer",
                Components =
                {
                    new CuiRawImageComponent { Color = t.Accent, Png = GetImage("kf_round_button.png") },
                    new CuiRectTransformComponent
                        { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-140 -15", OffsetMax = "-14 15" }
                }
            });
            container.Add(new CuiButton
            {
                Button = { Command = "kf.confirmpreform", Color = "0 0 0 0" },
                Text =
                {
                    Text = GetLocalizedMessage("btn.save", player), Font = FontBold, FontSize = 12,
                    Align = TextAnchor.MiddleCenter, Color = t.AccentText
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, Layer + ".footer.save", Layer + ".footer.save.btn");
        }

        private readonly Dictionary<ulong, string> _activeSection = new();

        private readonly Dictionary<ulong, string> _selectedElement = new();
        private readonly Dictionary<ulong, float> _moveStep = new();

        private string GetSelectedElement(BasePlayer player) =>
            _selectedElement.TryGetValue(player.userID, out var id) ? id : "killer_text";

        private float GetMoveStep(BasePlayer player) =>
            _moveStep.TryGetValue(player.userID, out var s) ? s : 2f;

        private string GetActiveSection(BasePlayer player) =>
            _activeSection.TryGetValue(player.userID, out var id) ? id : "general";

        private void UI_DrawSectionContent(BasePlayer player, string sectionId)
        {
            var t = GetTheme(player);
            var preform = _killfeedPreforms.TryGetValue(player, out var p) ? p : cfg;
            var container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = Layer + ".body",
                Parent = Layer,
                DestroyUi = Layer + ".body",
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = $"{SidebarW} {FooterH}", OffsetMax = $"0 {-HeaderH}"
                    }
                }
            });

            switch (sectionId)
            {
                case "general":
                    UI_SectionGeneral(ref container, t, preform, player);
                    break;
                case "layout":
                    UI_SectionLayout(ref container, t, preform, player);
                    break;
                case "killer":
                    UI_SectionText(ref container, t, preform.uISettings.KillerTextSettings, "killer", player);
                    break;
                case "target":
                    UI_SectionText(ref container, t, preform.uISettings.TargetTextSettings, "target", player);
                    break;
                case "distance":
                    UI_SectionText(ref container, t, preform.uISettings.DistanceTextSettings, "distance", player);
                    break;
                case "icons":
                    UI_SectionIcons(ref container, t, preform, player);
                    break;
                case "anim":
                    UI_SectionAnim(ref container, t, preform, player);
                    break;
                case "themes":
                    UI_SectionThemes(ref container, t, preform, player);
                    break;
                case "elements":

                    UI_BeginScrollBody(ref container, t, 516f, 54f, 8f);
                    UI_SectionElements(ref container, t, preform, player);
                    break;
                case "filters":
                    UI_SectionFilters(ref container, t, preform, player);
                    break;
            }

            container.Add(new CuiElement
            {
                Name = Layer + ".body.hdr.1716",
                Parent = Layer + ".body",
                Components =
                {
                    new CuiImageComponent { Color = t.Surface },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -54", OffsetMax = "0 0" }
                }
            });

            container.Add(new CuiElement
            {
                Name = Layer + ".body.sep.ZGpkAt==",
                Parent = Layer + ".body",
                Components =
                {
                    new CuiImageComponent { Color = t.Border },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -54", OffsetMax = "0 -53" }
                }
            });

            container.Add(new CuiElement
            {
                Name = Layer + ".body.ttl",
                Parent = Layer + ".body",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = GetLocalizedMessage("sec." + sectionId + ".title", player), Font = FontBold, FontSize = 13,
                        Align = TextAnchor.UpperLeft, Color = t.Accent
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "16 -30", OffsetMax = "-16 -12" }
                }
            });

            container.Add(new CuiElement
            {
                Name = Layer + ".body.sub",
                Parent = Layer + ".body",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = GetLocalizedMessage("sec." + sectionId + ".sub", player), Font = FontRegular, FontSize = 11,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "16 -48", OffsetMax = "-16 -32" }
                }
            });

            CuiHelper.AddUi(player, container);
        }

        private void UI_BeginScrollBody(ref CuiElementContainer container, Theme t, float contentHeight,
            float topInset = 0f, float bottomInset = 0f)
        {
            var wrap = Layer + ".body" + ".scrollwrap";
            container.Add(new CuiElement
            {
                Name = wrap,
                Parent = Layer + ".body",
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 {bottomInset}", OffsetMax = $"-8 {-topInset}" }
                }
            });

            container.Add(new CuiElement
            {
                Name = Layer + ".body" + ".scroll",
                Parent = wrap,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiScrollViewComponent
                    {
                        Horizontal = false,
                        Vertical = true,
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Clamped,
                        Inertia = true,
                        ScrollSensitivity = 24f,
                        ContentTransform = new CuiRectTransform
                        {
                            AnchorMin = "0 1", AnchorMax = "1 1",
                            OffsetMin = $"0 {-contentHeight}", OffsetMax = "0 0"
                        },
                        VerticalScrollbar = ThemedScrollbar(t)
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
        }

        private CuiScrollbar ThemedScrollbar(Theme t) => new CuiScrollbar
        {
            AutoHide = false,
            Size = 4f,
            HandleColor = t.Accent,
            HighlightColor = t.AccentH,
            PressedColor = t.Accent,
            TrackColor = t.Bg2
        };

        private string UI_Group(ref CuiElementContainer container, Theme t, string host, string id, string title,
            float top, float height)
        {
            container.Add(new CuiElement
            {
                Name = host + ".group." + id,
                Parent = host,
                DestroyUi = host + ".group." + id,
                Components =
                {
                    new CuiRawImageComponent { Color = t.Bg, Png = GetImage("kf_round_group.png") },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = $"16 {top - height}", OffsetMax = $"-16 {top}"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = host + ".group." + id,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "●", Font = FontBold, FontSize = 10,
                        Align = TextAnchor.UpperLeft, Color = t.Accent
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "14 -22", OffsetMax = "26 -8" }
                }
            });

            container.Add(new CuiElement
            {
                Parent = host + ".group." + id,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = title, Font = FontBold, FontSize = 10,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "26 -22", OffsetMax = "-14 -8" }
                }
            });

            return host + ".group." + id;
        }

        private void UI_Toggle(ref CuiElementContainer container, Theme t, string parent, string label, string desc,
            string field, bool on, float top, float x, float width, string toggleGroup = "toggle")
        {
            var row = parent + ".tg." + field;
            const float rowH = 38f;

            container.Add(new CuiButton
            {
                Button = { Command = $"kf.setvalue True {toggleGroup} {field}", Color = "0 0 0 0" },
                Text = { Text = "" },
                RectTransform =
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"{x} {top - rowH}", OffsetMax = $"{x + width} {top}"
                }
            }, parent, row);

            container.Add(new CuiElement
            {
                Parent = row,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = label, Font = FontBold, FontSize = 12,
                        Align = TextAnchor.UpperLeft, Color = t.Text
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -16", OffsetMax = "-46 0" }
                }
            });

            container.Add(new CuiElement
            {
                Parent = row,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = desc, Font = FontRegular, FontSize = 9,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -30", OffsetMax = "-46 -16" }
                }
            });

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = on ? t.Accent : t.Elev },
                RectTransform =
                    { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-36 -22", OffsetMax = "0 -2" }
            }, row, row + ".track");

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = on ? t.AccentText : t.Text2 },
                RectTransform =
                {
                    AnchorMin = on ? "1 0.5" : "0 0.5", AnchorMax = on ? "1 0.5" : "0 0.5",
                    OffsetMin = on ? "-17 -7" : "3 -7", OffsetMax = on ? "-3 7" : "17 7"
                }
            }, row + ".track", row + ".knob");
        }

        private void UI_Stepper(ref CuiElementContainer container, Theme t, string parent, string label, string key,
            float value, float step, float min, float max, float top, float x, float width, string relativeBase = null)
        {
            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = label, Font = FontRegular, FontSize = 9,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {top - 14}", OffsetMax = $"{x + width} {top}" }
                }
            });

            var decCommand = relativeBase != null
                ? $"kf.setvalue True {relativeBase} {FormatNum(-step)}"
                : $"kf.setvalue True {key} {FormatNum(Mathf.Clamp(value - step, min, max))}";
            var incCommand = relativeBase != null
                ? $"kf.setvalue True {relativeBase} {FormatNum(step)}"
                : $"kf.setvalue True {key} {FormatNum(Mathf.Clamp(value + step, min, max))}";

            container.Add(new CuiButton
            {
                Button = { Command = decCommand, Color = t.Elev },
                Text =
                {
                    Text = "−", Font = FontBold, FontSize = 16,
                    Align = TextAnchor.MiddleCenter, Color = t.Text
                },
                RectTransform =
                    { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {top - 42}", OffsetMax = $"{x + 26} {top - 16}" }
            }, parent, parent + ".step." + key + ".dec");

            container.Add(new CuiElement
            {
                Name = parent + ".step." + key + ".bg",
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = t.Bg2 },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x + 28} {top - 42}", OffsetMax = $"{x + width - 28} {top - 16}" }
                }
            });

            container.Add(new CuiElement
            {
                Parent = parent + ".step." + key + ".bg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = FormatNum(value), Font = FontBold, FontSize = 12,
                        Align = TextAnchor.MiddleCenter, Color = t.Accent, CharsLimit = 10,
                        Command = $"kf.setvalue True {key} "
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Command = incCommand, Color = t.Elev },
                Text =
                {
                    Text = "+", Font = FontBold, FontSize = 14,
                    Align = TextAnchor.MiddleCenter, Color = t.Text
                },
                RectTransform =
                    { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x + width - 26} {top - 42}", OffsetMax = $"{x + width} {top - 16}" }
            }, parent, parent + ".step." + key + ".inc");
        }

        private void UI_SelectButtons(ref CuiElementContainer container, Theme t, string parent, string label,
            string key, SelectOption[] options, string current, float top, float x, float width)
        {
            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = label, Font = FontRegular, FontSize = 9,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {top - 14}", OffsetMax = $"{x + width} {top}" }
                }
            });

            var btnTop = top - 16f;
            var btnBot = btnTop - 26f;
            var btnW = width / options.Length;

            for (var i = 0; i < options.Length; i++)
            {
                var isOn = options[i].Value == current;
                var bx = x + i * btnW;

                container.Add(new CuiButton
                {
                    Button = { Command = $"kf.setvalue True {key} {options[i].Value}", Color = isOn ? t.Accent : t.Elev },
                    Text =
                    {
                        Text = options[i].Text, Font = FontBold, FontSize = 11,
                        Align = TextAnchor.MiddleCenter, Color = isOn ? t.AccentText : t.Text2
                    },
                    RectTransform =
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{bx + 1} {btnBot}", OffsetMax = $"{bx + btnW - 1} {btnTop}" }
                }, parent, parent + ".sel." + key + "." + options[i].Value);
            }
        }

        private void UI_RgbaInput(ref CuiElementContainer container, Theme t, string parent, string label, string key,
            string value, float top, float x, float width)
        {
            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = label, Font = FontRegular, FontSize = 9,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {top - 14}", OffsetMax = $"{x + width} {top}" }
                }
            });

            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = NormalizeRgba(value) },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {top - 42}", OffsetMax = $"{x + 26} {top - 16}" }
                }
            });

            container.Add(new CuiElement
            {
                Name = parent + ".rgba." + key + ".bg",
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = t.Bg2 },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x + 30} {top - 42}", OffsetMax = $"{x + width} {top - 16}" }
                }
            });

            container.Add(new CuiElement
            {
                Parent = parent + ".rgba." + key + ".bg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = value, FontSize = 11, Font = FontRegular,
                        Align = TextAnchor.MiddleLeft, Color = t.Text, CharsLimit = 30,
                        Command = $"kf.setvalue True {key} "
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "6 0", OffsetMax = "-6 0" }
                }
            });
        }

        #endregion

        #region UI Sections

        private void UI_SectionGeneral(ref CuiElementContainer container, Theme t, ConfigData preform, BasePlayer player)
        {
            var c = preform.uISettings.Conditions;

            var g1 = UI_Group(ref container, t, Layer + ".body", "vis", GetLocalizedMessage("grp.visibility", player), -56f, 158f);
            const float colW = 220f;
            const float col1X = 14f;
            const float col2X = 248f;
            const float rowStep = 42f;

            UI_Toggle(ref container, t, g1, GetLocalizedMessage("tg.killer", player), GetLocalizedMessage("tg.killer.d", player), "showKiller",
                c.EnableKillerNickname, -28f, col1X, colW);
            UI_Toggle(ref container, t, g1, GetLocalizedMessage("tg.target", player), GetLocalizedMessage("tg.target.d", player), "showTarget",
                c.EnableTargetNickname, -28f, col2X, colW);

            UI_Toggle(ref container, t, g1, GetLocalizedMessage("tg.weapon", player), GetLocalizedMessage("tg.weapon.d", player), "showWeapon",
                c.EnableWeapon, -28f - rowStep, col1X, colW);
            UI_Toggle(ref container, t, g1, GetLocalizedMessage("tg.ammo", player), GetLocalizedMessage("tg.ammo.d", player), "showAmmo",
                c.EnableAmmo, -28f - rowStep, col2X, colW);

            UI_Toggle(ref container, t, g1, GetLocalizedMessage("tg.distance", player), GetLocalizedMessage("tg.distance.d", player), "showDistance",
                c.EnableDistance, -28f - rowStep * 2, col1X, colW);
            UI_Toggle(ref container, t, g1, GetLocalizedMessage("tg.headshot", player), GetLocalizedMessage("tg.headshot.d", player), "showHeadshot",
                c.EnableHeadshot, -28f - rowStep * 2, col2X, colW);

            var g2 = UI_Group(ref container, t, Layer + ".body", "behav", GetLocalizedMessage("grp.behaviour", player), -228f, 138f);

            UI_SelectButtons(ref container, t, g2, GetLocalizedMessage("lbl.direction", player),
                "direction",
                new[] { new SelectOption("down", GetLocalizedMessage("lbl.dir.down", player)), new SelectOption("up", GetLocalizedMessage("lbl.dir.up", player)) },
                c.FromUpToDown ? "down" : "up",
                -30f, 14f, 230f);

            UI_Stepper(ref container, t, g2, GetLocalizedMessage("lbl.maxkills", player), "maxkills",
                preform.uISettings.MaxPanels, 1, 1, 20, -30f, 256f, 230f);

            UI_Toggle(ref container, t, g2, GetLocalizedMessage("tg.private", player), GetLocalizedMessage("tg.private.d", player),
                "showPrivate", preform.PrivateKillsOnly, -84f, 14f, 472f, "toggle.cfg");
        }

        private void UI_SectionLayout(ref CuiElementContainer container, Theme t, ConfigData preform, BasePlayer player)
        {
            var o = preform.uISettings.OffsetSettings;

            var g1 = UI_Group(ref container, t, Layer + ".body", "anchor", GetLocalizedMessage("grp.anchor", player), -56f, 150f);

            UI_Stepper(ref container, t, g1, GetLocalizedMessage("lbl.offx", player), "minx", o.OffsetMinX, 5, -2000, 2000, -28f, 14f, 150f);
            UI_Stepper(ref container, t, g1, GetLocalizedMessage("lbl.offy", player), "miny", o.OffsetMinY, 5, -2000, 2000, -28f, 170f, 150f);
            UI_Stepper(ref container, t, g1, GetLocalizedMessage("lbl.indent", player), "indent", o.Indent, 1, 0, 50, -78f, 14f, 150f);

            UI_AnchorGrid(ref container, t, g1, preform.uISettings.AnchorSettings, 340f, -26f, player);

            var g2 = UI_Group(ref container, t, Layer + ".body", "bg", GetLocalizedMessage("grp.bg", player), -218f, 88f);

            UI_RgbaInput(ref container, t, g2, GetLocalizedMessage("lbl.bgcolor", player), "bgcolor",
                preform.uISettings.BGColor, -30f, 14f, 472f);
        }

        private void UI_TextInput(ref CuiElementContainer container, Theme t, string parent, string label, string key,
            string value, float top, float x, float width)
        {
            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = label, Font = FontRegular, FontSize = 9,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {top - 14}", OffsetMax = $"{x + width} {top}" }
                }
            });

            container.Add(new CuiElement
            {
                Name = parent + ".txt." + key + ".bg",
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = t.Bg2 },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {top - 42}", OffsetMax = $"{x + width} {top - 16}" }
                }
            });

            container.Add(new CuiElement
            {
                Parent = parent + ".txt." + key + ".bg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = value, FontSize = 11, Font = FontRegular,
                        Align = TextAnchor.MiddleLeft, Color = t.Text, CharsLimit = 64,
                        Command = $"kf.setvalue True {key} "
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "6 0", OffsetMax = "-6 0" }
                }
            });
        }

        private void UI_SectionText(ref CuiElementContainer container, Theme t, ConfigData.TextSettings s, string type,
            BasePlayer player)
        {

            var g1 = UI_Group(ref container, t, Layer + ".body", type + "_fmt", GetLocalizedMessage("grp.format", player), -56f, 134f);

            UI_TextInput(ref container, t, g1,
                GetLocalizedMessage(type == "distance" ? "lbl.tpl.meters" : "lbl.tpl.nick", player),
                type + "_text", s.TextFormat, -28f, 14f, 472f);

            UI_SelectButtons(ref container, t, g1, GetLocalizedMessage("lbl.align", player), type + "_text_align",
                new[] { new SelectOption("L", "L"), new SelectOption("M", "M"), new SelectOption("R", "R") },
                s.Alignment, -82f, 14f, 150f);

            UI_Stepper(ref container, t, g1, GetLocalizedMessage("lbl.size", player), type + "_fontsize", s.FontSize, 1, 6, 48, -82f, 256f, 150f);

            var g2 = UI_Group(ref container, t, Layer + ".body", type + "_clr", GetLocalizedMessage("grp.color", player), -204f, 134f);

            UI_RgbaInput(ref container, t, g2, GetLocalizedMessage("lbl.text_color", player), type + "_color", s.Color, -28f, 14f, 226f);
            UI_RgbaInput(ref container, t, g2, GetLocalizedMessage("lbl.outline_color", player), type + "_outlinecolor", s.OutlineColor,
                -28f, 256f, 226f);
            UI_TextInput(ref container, t, g2, GetLocalizedMessage("lbl.outline_dist", player), type + "_outlinedistance", s.OutlineDistance,
                -82f, 14f, 226f);

            if (type != "distance")
                UI_Stepper(ref container, t, g2, GetLocalizedMessage("lbl.maxnick", player), type + "_maxnick", s.MaxNickLength, 1, 0, 64,
                    -82f, 256f, 226f);
        }

        private void UI_SectionIcons(ref CuiElementContainer container, Theme t, ConfigData preform, BasePlayer player)
        {

            var g1 = UI_Group(ref container, t, Layer + ".body", "ic_hs", GetLocalizedMessage("grp.hs_icon", player), -56f, 134f);

            UI_TextInput(ref container, t, g1, GetLocalizedMessage("lbl.path_url", player), "headshotimage",
                preform.uISettings.HeadshotImage.Value as string ?? "", -28f, 14f, 360f);

            UI_IconPreview(ref container, t, g1, preform.uISettings.HeadshotImage.Value as string,
                preform.uISettings.HeadshotImage.Color, 392f, -28f);

            UI_RgbaInput(ref container, t, g1, GetLocalizedMessage("lbl.tint", player), "headshot_icon_color",
                preform.uISettings.HeadshotImage.Color, -82f, 14f, 360f);

            var g2 = UI_Group(ref container, t, Layer + ".body", "ic_death", GetLocalizedMessage("grp.death_icon", player), -204f, 134f);

            UI_TextInput(ref container, t, g2, GetLocalizedMessage("lbl.path_url", player), "deathimage",
                preform.DefaultDeathImage.Value as string ?? "", -28f, 14f, 360f);

            UI_IconPreview(ref container, t, g2, preform.DefaultDeathImage.Value as string,
                preform.DefaultDeathImage.Color, 392f, -28f);

            UI_RgbaInput(ref container, t, g2, GetLocalizedMessage("lbl.tint", player), "deathdefault_icon_color",
                preform.DefaultDeathImage.Color, -82f, 14f, 360f);
        }

        private void UI_IconPreview(ref CuiElementContainer container, Theme t, string parent, string image,
            string tint, float x, float top)
        {
            container.Add(new CuiElement
            {
                Name = parent + ".prev",
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = t.Bg2 },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {top - 60}", OffsetMax = $"{x + 60} {top}" }
                }
            });

            if (string.IsNullOrEmpty(image))
                return;

            if (image.Contains("assets/"))
                container.Add(new CuiElement
                {
                    Parent = parent + ".prev",
                    Components =
                    {
                        new CuiImageComponent { Color = NormalizeRgba(tint), Sprite = image },
                        new CuiRectTransformComponent
                            { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "6 6", OffsetMax = "-6 -6" }
                    }
                });
            else
                container.Add(new CuiElement
                {
                    Parent = parent + ".prev",
                    Components =
                    {
                        new CuiRawImageComponent { Color = NormalizeRgba(tint), Png = GetImage(image) },
                        new CuiRectTransformComponent
                            { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "6 6", OffsetMax = "-6 -6" }
                    }
                });
        }

        private void UI_SectionAnim(ref CuiElementContainer container, Theme t, ConfigData preform, BasePlayer player)
        {
            var s = preform.uISettings;

            var g1 = UI_Group(ref container, t, Layer + ".body", "an_time", GetLocalizedMessage("grp.timings", player), -56f, 170f);

            UI_Stepper(ref container, t, g1, GetLocalizedMessage("lbl.fadein", player), "fadein",
                s.FadeIn, 0.05f, 0f, 5f, -28f, 14f, 226f);

            UI_Stepper(ref container, t, g1, GetLocalizedMessage("lbl.lifetime", player), "lifetime",
                preform.Lifetime, 1, 1, 60, -28f, 256f, 226f);

            UI_Toggle(ref container, t, g1, GetLocalizedMessage("tg.animate", player), GetLocalizedMessage("tg.animate.d", player), "animate", s.AnimateFeed, -84f, 14f, 226f, "toggle.ui");

            UI_Toggle(ref container, t, g1, GetLocalizedMessage("tg.animate_exit", player), GetLocalizedMessage("tg.animate_exit.d", player), "animate_exit", s.AnimateFeedExit, -84f, 256f, 226f, "toggle.ui");

            var g2 = UI_Group(ref container, t, Layer + ".body", "an_slide", GetLocalizedMessage("grp.behaviour", player), -232f, 170f);

            UI_SelectButtons(ref container, t, g2, GetLocalizedMessage("lbl.anim_indir", player), "anim_indir",
                new[] { new SelectOption("Right", GetLocalizedMessage("opt.dir_right", player)), new SelectOption("Left", GetLocalizedMessage("opt.dir_left", player)) },
                s.AnimationInDirection, -28f, 14f, 226f);

            UI_SelectButtons(ref container, t, g2, GetLocalizedMessage("lbl.anim_outdir", player), "anim_outdir",
                new[] { new SelectOption("Right", GetLocalizedMessage("opt.dir_right", player)), new SelectOption("Left", GetLocalizedMessage("opt.dir_left", player)) },
                s.AnimationOutDirection, -28f, 256f, 226f);

            UI_Stepper(ref container, t, g2, GetLocalizedMessage("lbl.anim_dist", player), "anim_distance",
                s.AnimationDistance, 10f, 20f, 600f, -84f, 14f, 226f);

            UI_Stepper(ref container, t, g2, GetLocalizedMessage("lbl.anim_dur", player), "anim_duration",
                s.AnimationDuration, 0.05f, 0.05f, 1f, -84f, 256f, 226f);
        }

        private void UI_SectionFilters(ref CuiElementContainer container, Theme t, ConfigData preform, BasePlayer player)
        {
            var monuments = GetMonuments();
            var listH = Mathf.Max(64f, 28f + monuments.Count * 34f + 8f);
            UI_BeginScrollBody(ref container, t, Mathf.Max(398f, 138f + listH), 54f, 8f);
            UI_FiltersContent(ref container, t, preform, player, monuments, listH);
        }

        private void UI_RefreshFilters(BasePlayer player)
        {
            if (!_killfeedPreforms.TryGetValue(player, out var preform))
                return;

            var monuments = GetMonuments();
            var listH = Mathf.Max(64f, 28f + monuments.Count * 34f + 8f);
            var container = new CuiElementContainer();
            UI_FiltersContent(ref container, GetTheme(player), preform, player, monuments, listH);
            CuiHelper.AddUi(player, container);
        }

        private void UI_RefreshElements(BasePlayer player)
        {
            if (!_killfeedPreforms.TryGetValue(player, out var preform))
                return;

            var container = new CuiElementContainer();
            UI_SectionElements(ref container, GetTheme(player), preform, player);
            CuiHelper.AddUi(player, container);
        }

        private void UI_FiltersContent(ref CuiElementContainer container, Theme t, ConfigData preform, BasePlayer player,
            List<(string shortName, string display)> monuments, float listH)
        {
            const float rowH = 30f;
            const float rowStep = 34f;
            var host = Layer + ".body" + ".scroll";

            var g1 = UI_Group(ref container, t, host, "zm", GetLocalizedMessage("grp.zonemanager", player), -2f, 96f);
            UI_SelectButtons(ref container, t, g1, GetLocalizedMessage("lbl.zm_flag", player), "zm_flag",
                new[]
                {
                    new SelectOption("", GetLocalizedMessage("opt.off", player)),
                    new SelectOption("Custom1", "Custom1"),
                    new SelectOption("Custom2", "Custom2"),
                    new SelectOption("Custom3", "Custom3"),
                    new SelectOption("Custom4", "Custom4"),
                    new SelectOption("Custom5", "Custom5")
                },
                preform.ZoneManagerFlag ?? "", -28f, 14f, 468f);
            container.Add(new CuiElement
            {
                Parent = g1,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = ZoneManager != null ? GetLocalizedMessage("zm.loaded", player) : GetLocalizedMessage("zm.missing", player),
                        Font = FontRegular, FontSize = 9, Align = TextAnchor.UpperLeft,
                        Color = ZoneManager != null ? t.Text2 : "0.9 0.45 0.4 1"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "14 -90", OffsetMax = "-14 -74" }
                }
            });

            var g2 = UI_Group(ref container, t, host, "mons", GetLocalizedMessage("grp.monuments", player), -114f, listH);

            if (MonumentFinder == null)
            {
                UI_GroupNote(ref container, t, g2, GetLocalizedMessage("mon.missing", player));
                return;
            }

            if (monuments.Count == 0)
            {
                UI_GroupNote(ref container, t, g2, GetLocalizedMessage("mon.none", player));
                return;
            }

            var rowTop = -28f;
            foreach (var mon in monuments)
            {
                var on = preform.MonumentBlacklist != null &&
                         preform.MonumentBlacklist.Any(b => string.Equals(b, mon.shortName, StringComparison.OrdinalIgnoreCase));
                var row = g2 + ".mon." + mon.shortName;
                var label = string.Equals(mon.display, mon.shortName, StringComparison.OrdinalIgnoreCase)
                    ? mon.shortName
                    : $"{mon.display} ({mon.shortName})";

                container.Add(new CuiButton
                {
                    Button = { Command = $"kf.setvalue True monument_toggle {mon.shortName}", Color = on ? t.Accent : t.Bg2 },
                    Text = { Text = "" },
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = $"14 {rowTop - rowH}", OffsetMax = $"-14 {rowTop}"
                    }
                }, g2, row);

                container.Add(new CuiElement
                {
                    Parent = row,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = label, Font = FontRegular, FontSize = 12,
                            Align = TextAnchor.MiddleLeft, Color = on ? t.AccentText : t.Text
                        },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "12 0", OffsetMax = "-90 0" }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = row,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = on ? GetLocalizedMessage("mon.hidden", player) : GetLocalizedMessage("mon.shown", player),
                            Font = FontRegular, FontSize = 10,
                            Align = TextAnchor.MiddleRight, Color = on ? t.AccentText : t.Text2
                        },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "-12 0" }
                    }
                });

                rowTop -= rowStep;
            }
        }

        private void UI_GroupNote(ref CuiElementContainer container, Theme t, string parent, string text)
        {
            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = text, Font = FontRegular, FontSize = 11,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "14 -48", OffsetMax = "-14 -28" }
                }
            });
        }

        private List<(string shortName, string display)> GetMonuments()
        {
            var result = new List<(string shortName, string display)>();
            if (MonumentFinder == null)
                return result;

            if (!(MonumentFinder.Call("API_FindMonuments", "") is List<Dictionary<string, object>> list))
                return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in list)
            {
                var shortName = info.TryGetValue("ShortName", out var sn) ? sn as string : null;
                if (string.IsNullOrEmpty(shortName) && info.TryGetValue("Alias", out var al))
                    shortName = al as string;
                if (string.IsNullOrEmpty(shortName) || !seen.Add(shortName))
                    continue;

                var display = shortName;
                if (info.TryGetValue("Object", out var obj) && obj is MonumentInfo mi && mi.displayPhrase != null)
                {
                    var phrase = mi.displayPhrase.translated;
                    if (!string.IsNullOrEmpty(phrase))
                        display = phrase;
                }

                result.Add((shortName, display));
            }

            result.Sort((a, b) => string.Compare(a.display, b.display, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private void UI_SectionThemes(ref CuiElementContainer container, Theme t, ConfigData preform, BasePlayer player)
        {
            var g1 = UI_Group(ref container, t, Layer + ".body", "th_presets", GetLocalizedMessage("grp.presets", player), -56f, 170f);

            var cards = new[]
            {
                new ThemeCard(UITheme.Rust, "theme.rust", "theme.rust.d"),
                new ThemeCard(UITheme.Midnight, "theme.midnight", "theme.midnight.d"),
                new ThemeCard(UITheme.Neon, "theme.neon", "theme.neon.d")
            };

            const float cardW = 150f;
            const float cardH = 120f;
            for (var i = 0; i < cards.Length; i++)
            {
                var theme = _themes[cards[i].Theme];
                var isActive = theme == t;
                var cx = 14f + i * (cardW + 8f);
                var card = g1 + ".card." + cards[i].Theme;

                container.Add(new CuiElement
                {
                    Name = card,
                    Parent = g1,
                    Components =
                    {
                        new CuiImageComponent { Color = theme.Bg2 },
                        new CuiOutlineComponent
                            { Color = isActive ? t.Accent : t.Border, Distance = isActive ? "2 2" : "1 1" },
                        new CuiRectTransformComponent
                            { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{cx} -{30 + cardH}", OffsetMax = $"{cx + cardW} -30" }
                    }
                });

                container.Add(new CuiPanel
                {
                    Image = { Color = theme.Accent },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -22", OffsetMax = "0 0" }
                }, card);

                container.Add(new CuiElement
                {
                    Parent = card,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = GetLocalizedMessage(cards[i].NameKey, player), Font = FontBold, FontSize = 12,
                            Align = TextAnchor.MiddleLeft, Color = theme.AccentText
                        },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "10 -22", OffsetMax = "-28 0" }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = card,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = GetLocalizedMessage(cards[i].DescKey, player), Font = FontRegular, FontSize = 9,
                            Align = TextAnchor.UpperLeft, Color = theme.Text2
                        },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "12 -66", OffsetMax = "-12 -30" }
                    }
                });

                var swatches = new[] { theme.Accent, theme.Surface, theme.Elev, theme.Border, theme.Text };
                for (var s = 0; s < swatches.Length; s++)
                    container.Add(new CuiPanel
                    {
                        Image = { Color = swatches[s] },
                        RectTransform =
                            { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{12 + s * 26} 12", OffsetMax = $"{12 + s * 26 + 20} 32" }
                    }, card);

                if (isActive)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = t.Accent },
                        RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-22 -22", OffsetMax = "-4 -4" }
                    }, card, card + ".chk");

                    container.Add(new CuiElement
                    {
                        Parent = card + ".chk",
                        Components =
                        {
                            new CuiImageComponent { Color = t.AccentText, Sprite = "assets/icons/check.png" },
                            new CuiRectTransformComponent
                                { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-5 -5", OffsetMax = "5 5" }
                        }
                    });
                }

                container.Add(new CuiButton
                {
                    Button = { Command = $"kf.theme {cards[i].Theme}", Color = "0 0 0 0" },
                    Text = { Text = "" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, card);
            }
        }

        private static readonly string[] EditableElements =
        {
            "killer_text", "killer_avatar", "target_text", "target_avatar", "weapon_icon", "ammo_icon", "distance_text", "headshot_icon"
        };

        private static readonly string[] NoKillerElements =
        {
            "nokiller_text", "nokiller_icon"
        };

        private static readonly string[] AllEditableElements =
        {
            "killer_text", "killer_avatar", "target_text", "target_avatar", "weapon_icon", "ammo_icon", "distance_text", "headshot_icon",
            "nokiller_text", "nokiller_icon"
        };

        private void UI_SectionElements(ref CuiElementContainer container, Theme t, ConfigData preform, BasePlayer player)
        {
            var selected = GetSelectedElement(player);
            if (!preform.uISettings.Elements.ContainsKey(selected))
                selected = "killer_text";

            var scroll = Layer + ".body" + ".scroll";

            var g = UI_Group(ref container, t, scroll, "el", GetLocalizedMessage("grp.el_list", player), -2f, 490f);

            var rowTop = -30f;
            foreach (var key in AllEditableElements)
            {
                var isSel = key == selected;
                var row = g + ".row." + key;

                container.Add(new CuiButton
                {
                    Button = { Command = $"kf.setvalue True selection.update {key}", Color = isSel ? t.Elev : t.Bg2 },
                    Text = { Text = "" },
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"12 {rowTop - 30}", OffsetMax = $"198 {rowTop}"
                    }
                }, g, row);

                if (isSel)
                    container.Add(new CuiElement
                    {
                        Parent = row,
                        Components =
                        {
                            new CuiImageComponent { Color = t.Accent },
                            new CuiRectTransformComponent
                                { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "3 0" }
                        }
                    });

                container.Add(new CuiElement
                {
                    Parent = row,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = GetLocalizedMessage("el." + key, player), Font = FontRegular, FontSize = 12,
                            Align = TextAnchor.MiddleLeft, Color = isSel ? t.Accent : t.Text
                        },
                        new CuiRectTransformComponent
                            { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "12 0", OffsetMax = "-8 0" }
                    }
                });

                rowTop -= 34f;
            }

            UI_DrawElementEditor(ref container, t, preform, player, selected);
        }

        private void UI_DrawElementEditor(ref CuiElementContainer container, Theme t, ConfigData preform,
            BasePlayer player, string selected)
        {
            var el = preform.uISettings.Elements[selected];
            var group = Layer + ".body" + ".scroll" + ".group.el";
            var g2 = group + ".editor";

            container.Add(new CuiElement
            {
                Name = g2,
                Parent = group,
                DestroyUi = g2,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "206 -482", OffsetMax = "-8 -30" }
                }
            });

            container.Add(new CuiElement
            {
                Parent = g2,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = GetLocalizedMessage("grp.el_edit", player) + " · " + GetLocalizedMessage("el." + selected, player),
                        Font = FontBold, FontSize = 10, Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "4 -14", OffsetMax = "-4 0" }
                }
            });

            UI_Stepper(ref container, t, g2, GetLocalizedMessage("lbl.pos_x", player), $"element.x {selected}", el.X, GetMoveStep(player),
                -5000, 5000, -40f, 4f, 152f, $"element.adjust {selected} x");
            UI_Stepper(ref container, t, g2, GetLocalizedMessage("lbl.pos_y", player), $"element.y {selected}", el.Y, GetMoveStep(player),
                -5000, 5000, -40f, 166f, 152f, $"element.adjust {selected} y");
            UI_Stepper(ref container, t, g2, GetLocalizedMessage("lbl.width", player), $"element.width {selected}", el.Width, 1,
                0, 5000, -94f, 4f, 152f, $"element.adjust {selected} width");
            UI_Stepper(ref container, t, g2, GetLocalizedMessage("lbl.height", player), $"element.height {selected}", el.Height, 1,
                0, 5000, -94f, 166f, 152f, $"element.adjust {selected} height");

            UI_SelectButtons(ref container, t, g2, GetLocalizedMessage("lbl.move_step", player), "element.move.step",
                new[] { new SelectOption("2", "2"), new SelectOption("5", "5"), new SelectOption("10", "10"), new SelectOption("25", "25") },
                FormatNum(GetMoveStep(player)), -148f, 4f, 318f);

            container.Add(new CuiElement
            {
                Parent = g2,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = GetLocalizedMessage("lbl.move", player), Font = FontRegular, FontSize = 9,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "4 -212" , OffsetMax = "160 -198" }
                }
            });

            UI_ArrowPad(ref container, t, g2, selected, GetMoveStep(player), 4f, -214f);

            if (selected == "nokiller_text")
                UI_TextInput(ref container, t, g2, GetLocalizedMessage("lbl.nokiller_fmt", player),
                    "nokiller_format", preform.uISettings.NoKillerTextFormat ?? "{0} | {1}", -214f, 140f, 174f);
        }

        private void UI_RefreshElementEditor(BasePlayer player)
        {
            if (!_killfeedPreforms.TryGetValue(player, out var preform))
                return;

            var selected = GetSelectedElement(player);
            if (!preform.uISettings.Elements.ContainsKey(selected))
                return;

            var container = new CuiElementContainer();
            UI_DrawElementEditor(ref container, GetTheme(player), preform, player, selected);
            CuiHelper.AddUi(player, container);
        }

        private void UI_ArrowPad(ref CuiElementContainer container, Theme t, string parent, string elementKey,
            float step, float x, float top)
        {

            var pads = new[]
            {
                new ArrowButton(32f, 0f, "up", "↑"),
                new ArrowButton(0f, -32f, "left", "←"),
                new ArrowButton(64f, -32f, "right", "→"),
                new ArrowButton(32f, -64f, "down", "↓")
            };

            foreach (var p in pads)
                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"kf.setvalue True element.move {elementKey} {p.Direction} {FormatNum(step)}",
                        Color = t.Elev
                    },
                    Text =
                    {
                        Text = p.Glyph, Font = FontBold, FontSize = 16,
                        Align = TextAnchor.MiddleCenter, Color = t.Text
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"{x + p.OffsetX} {top + p.OffsetY - 28}", OffsetMax = $"{x + p.OffsetX + 28} {top + p.OffsetY}"
                    }
                }, parent, parent + ".arrow." + p.Direction);
        }

        private void UI_AnchorGrid(ref CuiElementContainer container, Theme t, string parent,
            AnchorSettings current, float x, float top, BasePlayer player)
        {
            var cells = new[]
            {
                new AnchorCell(0, 0, AnchorType.LeftTop), new AnchorCell(1, 0, AnchorType.CenterTop), new AnchorCell(2, 0, AnchorType.RightTop),
                new AnchorCell(0, 1, AnchorType.LeftMiddle), new AnchorCell(1, 1, AnchorType.Middle), new AnchorCell(2, 1, AnchorType.RightMiddle),
                new AnchorCell(0, 2, AnchorType.LeftBottom), new AnchorCell(1, 2, AnchorType.CenterBottom), new AnchorCell(2, 2, AnchorType.RightBottom)
            };

            const float cell = 30f;
            const float gap = 4f;

            var activeType = ResolveAnchorType(current);

            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = GetLocalizedMessage("lbl.anchor", player), Font = FontRegular, FontSize = 9,
                        Align = TextAnchor.UpperLeft, Color = t.Text2
                    },
                    new CuiRectTransformComponent
                        { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {top - 14}", OffsetMax = $"{x + 110} {top}" }
                }
            });

            var gridTop = top - 18f;
            foreach (var cellData in cells)
            {
                var cx = x + cellData.Column * (cell + gap);
                var cyTop = gridTop - cellData.Row * (cell + gap);
                var isOn = cellData.Type == activeType;

                container.Add(new CuiButton
                {
                    Button = { Command = $"kf.setvalue True set_anchor {cellData.Type}", Color = isOn ? t.Accent : t.Elev },
                    Text = { Text = "" },
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"{cx} {cyTop - cell}", OffsetMax = $"{cx + cell} {cyTop}"
                    }
                }, parent, parent + ".anchor." + cellData.Type);
            }
        }

        private AnchorType ResolveAnchorType(AnchorSettings current)
        {
            foreach (var kv in _anchors)
                if (kv.Value.Key == current.AnchorMin && kv.Value.Value == current.AnchorMax)
                    return kv.Key;

            return AnchorType.None;
        }

        private string FormatNum(float v)
        {
            if (Mathf.Approximately(v, Mathf.Round(v)))
                return ((int)Mathf.Round(v)).ToString();
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private float ParseFloat(string value)
        {
            value = (value ?? "").Trim().Replace(',', '.');
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        private int ParseInt(string value)
        {
            return (int)Mathf.Round(ParseFloat(value));
        }

        private string NormalizeRgba(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "0 0 0 1";

            value = value.Trim();

            if (value.StartsWith("#"))
                return HexToRgba(value);

            var parts = value.Split(' ');
            if (parts.Length == 3)
                return value + " 1";
            return value;
        }

        private string HexToRgba(string hex)
        {
            hex = hex.Trim().TrimStart('#');

            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);

            if (hex.Length != 6 && hex.Length != 8)
                return "1 1 1 1";

            if (!int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
                !int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
                !int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                return "1 1 1 1";

            var a = 255;
            if (hex.Length == 8 &&
                !int.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
                a = 255;

            return $"{(r / 255f).ToString("0.###", CultureInfo.InvariantCulture)} " +
                   $"{(g / 255f).ToString("0.###", CultureInfo.InvariantCulture)} " +
                   $"{(b / 255f).ToString("0.###", CultureInfo.InvariantCulture)} " +
                   $"{(a / 255f).ToString("0.###", CultureInfo.InvariantCulture)}";
        }

        #endregion

        #region Kill Feed Rendering

        private void UI_DrawKills(BasePlayer player, Dictionary<KillData, int> kills, bool isTesting = false,
            bool animate = false, string newGuid = null)
        {
            if (player == null || player.IsDestroyed)
                return;

            if (!CanSeeKills(player) && !isTesting)
                return;

            if (kills == null || kills.Count == 0)
            {
                CuiHelper.DestroyUi(player, LayerFeed);
                _platePos.Remove(player.userID);
                return;
            }

            ConfigData.UISettings settings;
            try
            {
                settings = isTesting && _killfeedPreforms.ContainsKey(player)
                    ? _killfeedPreforms[player].uISettings
                    : cfg.uISettings;
            }
            catch
            {
                settings = cfg.uISettings;
            }

            if (settings?.Elements == null || !settings.Elements.ContainsKey("kill_panel"))
                return;

            var killPanel = settings.Elements["kill_panel"];
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0" },
                RectTransform =
                {
                    AnchorMin = settings.AnchorSettings.AnchorMin,
                    AnchorMax = settings.AnchorSettings.AnchorMax,
                    OffsetMin = $"{settings.OffsetSettings.OffsetMinX} {settings.OffsetSettings.OffsetMinY}",
                    OffsetMax = $"{settings.OffsetSettings.OffsetMaxX} {settings.OffsetSettings.OffsetMaxY}"
                }
            }, "Hud", LayerFeed, LayerFeed);

            var selectedElement = GetSelectedElement(player);
            var selectedIsNoKiller = NoKillerElements.Contains(selectedElement);
            var outlineDrawn = false;

            var indent = settings.OffsetSettings.Indent;
            var fromUp = settings.Conditions.FromUpToDown;

            var ordered = kills.OrderByDescending(x => x.Value).ToList();
            var doAnim = animate && settings.AnimateFeed && !isTesting && !string.IsNullOrEmpty(newGuid)
                && ordered.Count > 0 && ordered[0].Key.Guid == newGuid;

            Dictionary<string, (float ax, float ay, float bx, float by)> live = null;
            var drawn = doAnim ? new HashSet<string>() : null;
            var inShift = doAnim ? DirSign(settings.AnimationInDirection) * settings.AnimationDistance : 0f;
            if (doAnim)
            {
                if (!_platePos.TryGetValue(player.userID, out live))
                {
                    live = new Dictionary<string, (float, float, float, float)>();
                    _platePos[player.userID] = live;
                }
            }

            var i = 0;
            foreach (var x in ordered)
            {
                string min, max;
                if (doAnim)
                {
                    var fMin = ParseOffset(killPanel.GetOffsetMinWithIntend(i, indent, fromUp));
                    var fMax = ParseOffset(killPanel.GetOffsetMaxWithIntend(i, indent, fromUp));
                    float sax, say, sbx, sby;
                    if (x.Key.Guid == newGuid)
                    {
                        sax = fMin.x + inShift;
                        say = fMin.y;
                        sbx = fMax.x + inShift;
                        sby = fMax.y;
                    }
                    else if (live.TryGetValue(x.Key.Guid, out var lp))
                    {
                        sax = lp.ax;
                        say = lp.ay;
                        sbx = lp.bx;
                        sby = lp.by;
                    }
                    else
                    {
                        sax = fMin.x;
                        say = fMin.y;
                        sbx = fMax.x;
                        sby = fMax.y;
                    }

                    min = $"{FormatNum(sax)} {FormatNum(say)}";
                    max = $"{FormatNum(sbx)} {FormatNum(sby)}";
                    live[x.Key.Guid] = (sax, say, sbx, sby);
                    drawn.Add(x.Key.Guid);
                }
                else
                {
                    min = killPanel.GetOffsetMinWithIntend(i, indent, fromUp);
                    max = killPanel.GetOffsetMaxWithIntend(i, indent, fromUp);
                }

                if (x.Key.IsNoKiller)
                    UI_GetNoKillerKill(ref container, player, settings, min, max, x.Key);
                else
                    UI_GetDefaultKill(ref container, player, settings, min, max, x.Key);

                if (isTesting && !outlineDrawn && x.Key.IsNoKiller == selectedIsNoKiller)
                {
                    UI_DrawElementOutlines(ref container, settings, x.Key, selectedElement);
                    outlineDrawn = true;
                }

                x.Key.NeedFadeIn = false;
                if (i >= settings.MaxPanels)
                    break;
                i++;
            }

            if (doAnim)
                foreach (var stale in live.Keys.Where(k => !drawn.Contains(k)).ToList())
                    live.Remove(stale);
            else
                _platePos.Remove(player.userID);

            CuiHelper.AddUi(player, container);

            if (doAnim)
                AnimateFeedIn(player, ordered, settings);
        }

        private string ShiftOffsetX(string offset, float dx)
        {
            var p = ParseOffset(offset);
            return $"{FormatNum(p.x + dx)} {FormatNum(p.y)}";
        }

        private float DirSign(string dir) => string.Equals(dir, "Left", StringComparison.OrdinalIgnoreCase) ? -1f : 1f;

        private void AnimateFeedIn(BasePlayer player, List<KeyValuePair<KillData, int>> ordered, ConfigData.UISettings settings)
        {
            if (player == null || settings?.Elements == null || !settings.Elements.ContainsKey("kill_panel"))
                return;
            if (!_platePos.TryGetValue(player.userID, out var live))
                return;

            var killPanel = settings.Elements["kill_panel"];
            var indent = settings.OffsetSettings.Indent;
            var fromUp = settings.Conditions.FromUpToDown;
            var steps = Mathf.Clamp(Mathf.RoundToInt(settings.AnimationDuration / ANIM_INTERVAL), 2, 100);

            var guids = new List<string>();
            var startMin = new List<(float x, float y)>();
            var startMax = new List<(float x, float y)>();
            var finalMin = new List<(float x, float y)>();
            var finalMax = new List<(float x, float y)>();

            var i = 0;
            foreach (var x in ordered)
            {
                var g = x.Key.Guid;
                var fMin = ParseOffset(killPanel.GetOffsetMinWithIntend(i, indent, fromUp));
                var fMax = ParseOffset(killPanel.GetOffsetMaxWithIntend(i, indent, fromUp));
                finalMin.Add(fMin);
                finalMax.Add(fMax);

                if (live.TryGetValue(g, out var lp))
                {
                    startMin.Add((lp.ax, lp.ay));
                    startMax.Add((lp.bx, lp.by));
                }
                else
                {
                    startMin.Add(fMin);
                    startMax.Add(fMax);
                }

                guids.Add(g);
                if (i >= settings.MaxPanels)
                    break;
                i++;
            }

            var userId = player.userID;
            if (_animCoroutines.TryGetValue(userId, out var existing) && existing != null)
                _feedBehaviour.StopCoroutine(existing);

            if (_feedBehaviour != null)
                _animCoroutines[userId] = _feedBehaviour.StartCoroutine(
                    AnimateInRoutine(player, userId, guids, startMin, startMax, finalMin, finalMax, steps));
        }

        private IEnumerator AnimateInRoutine(BasePlayer player, ulong userId, List<string> guids,
            List<(float x, float y)> startMin, List<(float x, float y)> startMax,
            List<(float x, float y)> finalMin, List<(float x, float y)> finalMax, int steps)
        {
            for (var step = 1; step <= steps; step++)
            {
                if (player == null || !player.IsConnected)
                {
                    _animCoroutines.Remove(userId);
                    yield break;
                }

                var tNorm = Mathf.Clamp01((float)step / steps);
                var tt = 1f - (1f - tNorm) * (1f - tNorm);

                _platePos.TryGetValue(userId, out var live);

                var container = new CuiElementContainer();
                for (var idx = 0; idx < guids.Count; idx++)
                {
                    var mx = Mathf.Lerp(startMin[idx].x, finalMin[idx].x, tt);
                    var my = Mathf.Lerp(startMin[idx].y, finalMin[idx].y, tt);
                    var Mx = Mathf.Lerp(startMax[idx].x, finalMax[idx].x, tt);
                    var My = Mathf.Lerp(startMax[idx].y, finalMax[idx].y, tt);

                    container.Add(new CuiElement
                    {
                        Name = LayerFeed + ".kill." + guids[idx],
                        Update = true,
                        Components =
                        {
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                                OffsetMin = $"{FormatNum(mx)} {FormatNum(my)}",
                                OffsetMax = $"{FormatNum(Mx)} {FormatNum(My)}"
                            }
                        }
                    });

                    if (live != null)
                        live[guids[idx]] = (mx, my, Mx, My);
                }

                CuiHelper.AddUi(player, container);

                yield return CoroutineEx.waitForSeconds(ANIM_INTERVAL);
            }

            _animCoroutines.Remove(userId);
        }

        private void AnimateFeedOut(List<(string guid, int index)> expiring)
        {
            var settings = cfg.uISettings;

            if (settings?.Elements == null || !settings.Elements.ContainsKey("kill_panel") || expiring == null || expiring.Count == 0)
            {
                _feedBehaviour?.RefreshFeed();
                return;
            }

            var killPanel = settings.Elements["kill_panel"];
            var indent = settings.OffsetSettings.Indent;
            var fromUp = settings.Conditions.FromUpToDown;

            var distance = settings.AnimationDistance;
            var steps = Mathf.Clamp(Mathf.RoundToInt(settings.AnimationDuration / ANIM_INTERVAL), 2, 100);
            var outShift = DirSign(settings.AnimationOutDirection) * distance;

            var startMin = new List<(float x, float y)>();
            var startMax = new List<(float x, float y)>();
            var finalMin = new List<(float x, float y)>();
            var finalMax = new List<(float x, float y)>();

            for (var i = 0; i < expiring.Count; i++)
            {
                var sMin = ParseOffset(killPanel.GetOffsetMinWithIntend(expiring[i].index, indent, fromUp));
                var sMax = ParseOffset(killPanel.GetOffsetMaxWithIntend(expiring[i].index, indent, fromUp));
                startMin.Add(sMin);
                startMax.Add(sMax);
                finalMin.Add((sMin.x + outShift, sMin.y));
                finalMax.Add((sMax.x + outShift, sMax.y));
            }

            if (_exitCoroutine != null)
                _feedBehaviour.StopCoroutine(_exitCoroutine);

            if (_feedBehaviour == null)
            {
                _feedBehaviour?.RefreshFeed();
                return;
            }

            _exitCoroutine = _feedBehaviour.StartCoroutine(
                AnimateOutRoutine(expiring, startMin, startMax, finalMin, finalMax, steps));
        }

        private IEnumerator AnimateOutRoutine(List<(string guid, int index)> expiring,
            List<(float x, float y)> startMin, List<(float x, float y)> startMax,
            List<(float x, float y)> finalMin, List<(float x, float y)> finalMax, int steps)
        {
            foreach (var target in BasePlayer.activePlayerList)
            {
                if (target == null || !target.IsConnected)
                    continue;
                if (!CanSeeKills(target))
                    continue;
                if (_killfeedPreforms.ContainsKey(target))
                    continue;

                foreach (var e in expiring)
                    CuiHelper.DestroyUi(target, LayerFeed + ".kill." + e.guid);
            }

            for (var step = 1; step <= steps; step++)
            {
                var tNorm = Mathf.Clamp01((float)step / steps);
                var tt = 1f - (1f - tNorm) * (1f - tNorm);

                foreach (var target in BasePlayer.activePlayerList)
                {
                    if (target == null || !target.IsConnected)
                        continue;
                    if (!CanSeeKills(target))
                        continue;
                    if (_killfeedPreforms.ContainsKey(target))
                        continue;

                    var container = new CuiElementContainer();
                    for (var i = 0; i < expiring.Count; i++)
                    {
                        var mx = Mathf.Lerp(startMin[i].x, finalMin[i].x, tt);
                        var my = Mathf.Lerp(startMin[i].y, finalMin[i].y, tt);
                        var Mx = Mathf.Lerp(startMax[i].x, finalMax[i].x, tt);
                        var My = Mathf.Lerp(startMax[i].y, finalMax[i].y, tt);

                        container.Add(new CuiElement
                        {
                            Name = LayerFeed + ".kill." + expiring[i].guid,
                            Update = true,
                            Components =
                            {
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                                    OffsetMin = $"{FormatNum(mx)} {FormatNum(my)}",
                                    OffsetMax = $"{FormatNum(Mx)} {FormatNum(My)}"
                                }
                            }
                        });
                    }

                    CuiHelper.AddUi(target, container);
                }

                yield return CoroutineEx.waitForSeconds(ANIM_INTERVAL);
            }

            _exitCoroutine = null;
            _feedBehaviour?.RefreshFeed();
        }

        private (float x, float y) ParseOffset(string offset)
        {
            var parts = (offset ?? "").Split(' ');
            var x = parts.Length > 0 ? ParseFloat(parts[0]) : 0f;
            var y = parts.Length > 1 ? ParseFloat(parts[1]) : 0f;
            return (x, y);
        }

        private void UI_DrawElementOutlines(ref CuiElementContainer container, ConfigData.UISettings settings,
            KillData kill, string selected)
        {
            var root = LayerFeed + $".kill.{kill.Guid}";

            Dictionary<string, string> parents;
            string[] keys;

            if (kill.IsNoKiller)
            {
                parents = new Dictionary<string, string>
                {
                    ["nokiller_text"] = root,
                    ["nokiller_icon"] = root
                };
                keys = NoKillerElements;
            }
            else
            {
                parents = new Dictionary<string, string>
                {
                    ["killer_text"] = root + ".killer.div",
                    ["killer_avatar"] = root + ".killer.div",
                    ["target_text"] = root + ".target.div",
                    ["target_avatar"] = root + ".target.div",
                    ["weapon_icon"] = root + ".weapon.div",
                    ["ammo_icon"] = root + ".weapon.div",
                    ["distance_text"] = root + ".weapon.div",
                    ["headshot_icon"] = root + ".weapon.div"
                };
                keys = EditableElements;
            }

            foreach (var key in keys)
            {
                if (!settings.Elements.TryGetValue(key, out var el) || !parents.TryGetValue(key, out var parent))
                    continue;

                var isSel = key == selected;
                var markerName = parent + ".outline." + key;

                container.Add(new CuiElement
                {
                    Name = markerName,
                    Parent = parent,
                    Components =
                    {
                        new CuiImageComponent { Color = isSel ? "0.93 0.55 0.2 0.16" : "1 1 1 0.03" },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = el.OffsetMin, OffsetMax = el.OffsetMax
                        }
                    }
                });

                var color = isSel ? "0.93 0.55 0.2 1" : "1 1 1 0.25";
                var width = isSel ? 2f : 1f;
                UI_GetOutline(ref container, markerName, color, width, true, true, true, true);
            }
        }

        private void UI_GetNoKillerKill(ref CuiElementContainer container, BasePlayer player,
            ConfigData.UISettings settings, string offsetmin, string offsetmax, KillData data)
        {
            var root = LayerFeed + $".kill.{data.Guid}";

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                FadeOut = settings.AnimateFeedExit ? settings.AnimationDuration : 0f,
                Image = { Color = settings.BGColor, FadeIn = data.NeedFadeIn ? settings.FadeIn : 0f },
                RectTransform =
                {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = offsetmin, OffsetMax = offsetmax
                }
            }, LayerFeed, root);

            var textEl = settings.Elements.TryGetValue("nokiller_text", out var te)
                ? te
                : settings.Elements["killer_text"];

            var victim = data.Target.Name;
            var reason = GetIdealNameForFeed(data.Killer.Name, lang.GetLanguage(player.UserIDString));
            string noKillerText;
            try
            {
                noKillerText = string.Format(settings.NoKillerTextFormat ?? "{0} | {1}", victim, reason);
            }
            catch
            {
                noKillerText = $"{victim} | {reason}";
            }

            container.Add(new CuiElement
            {
                Name = root + ".nokiller.text",
                Parent = root,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = noKillerText,
                        Font = settings.KillerTextSettings.Font, FontSize = settings.KillerTextSettings.FontSize,
                        Align = settings.KillerTextSettings.GetAlign(), Color = settings.KillerTextSettings.Color,
                        FadeIn = data.NeedFadeIn ? settings.FadeIn : 0f
                    },
                    new CuiOutlineComponent
                    {
                        Distance = settings.KillerTextSettings.OutlineDistance,
                        Color = settings.KillerTextSettings.OutlineColor
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                        OffsetMin = textEl.OffsetMin, OffsetMax = textEl.OffsetMax
                    }
                }
            });

            if (data.Weapon?.Image == null || data.Weapon.Image.Type == ImageType.None ||
                data.Weapon.Image.Type == ImageType.NotNeed)
                return;

            var iconEl = settings.Elements.TryGetValue("nokiller_icon", out var ie)
                ? ie
                : settings.Elements["weapon_icon"];

            UI_GetKillImage(ref container, settings, data, data.Weapon.Image, settings.HeadshotImage?.Color,
                root, iconEl.OffsetMin, iconEl.OffsetMax, root + ".nokiller.icon");
        }

        private void UI_GetDefaultKill(ref CuiElementContainer container, BasePlayer player,
            ConfigData.UISettings settings, string offsetmin, string offsetmax, KillData kill)
        {
            container.Add(new CuiPanel
            {
                FadeOut = settings.AnimateFeedExit ? settings.AnimationDuration : 0f,
                Image = { Color = settings.BGColor, FadeIn = kill.NeedFadeIn ? settings.FadeIn : 0f },
                RectTransform =
                {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = offsetmin, OffsetMax = offsetmax
                }
            }, LayerFeed, LayerFeed + $".kill.{kill.Guid}");

            var killerText = settings.Elements["killer_text"];
            var targetText = settings.Elements["target_text"];
            var weaponIcon = settings.Elements["weapon_icon"];
            var ammoIcon = settings.Elements["ammo_icon"];
            var distanceText = settings.Elements["distance_text"];
            var headshotIcon = settings.Elements["headshot_icon"];
            var conditions = settings.Conditions;

            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0" },
                RectTransform =
                {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = "-176.078 -12.408", OffsetMax = "-60.122 12.408"
                }
            }, LayerFeed + $".kill.{kill.Guid}", LayerFeed + $".kill.{kill.Guid}" + ".killer.div");

            if (conditions.EnableKillerNickname)
                container.Add(new CuiElement
                {
                    Name = LayerFeed + $".kill.{kill.Guid}" + ".killer.div" + ".killertext.KillFeed",
                    Parent = LayerFeed + $".kill.{kill.Guid}" + ".killer.div",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = string.Format(settings.KillerTextSettings.TextFormat,
                                settings.KillerTextSettings.Trim(GetIdealNameForFeed(kill.Killer.Name, lang.GetLanguage(player.UserIDString)))),
                            Font = settings.KillerTextSettings.Font, FontSize = settings.KillerTextSettings.FontSize,
                            Align = settings.KillerTextSettings.GetAlign(), Color = settings.KillerTextSettings.Color,
                            FadeIn = kill.NeedFadeIn ? settings.FadeIn : 0f
                        },
                        new CuiOutlineComponent
                        {
                            Distance = settings.KillerTextSettings.OutlineDistance,
                            Color = settings.KillerTextSettings.OutlineColor
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = killerText.OffsetMin, OffsetMax = killerText.OffsetMax
                        }
                    }
                });

            if (conditions.EnableKillerAvatar && settings.Elements.TryGetValue("killer_avatar", out var killerAvatar))
                UI_GetAvatar(ref container, settings, kill, kill.KillerAvatar,
                    LayerFeed + $".kill.{kill.Guid}" + ".killer.div",
                    killerAvatar.OffsetMin, killerAvatar.OffsetMax,
                    LayerFeed + $".kill.{kill.Guid}" + ".killer.div" + ".avatar");

            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0" },
                RectTransform =
                {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = "60.078 -12.408", OffsetMax = "176.122 12.408"
                }
            }, LayerFeed + $".kill.{kill.Guid}", LayerFeed + $".kill.{kill.Guid}" + ".target.div");

            if (conditions.EnableTargetNickname)
                container.Add(new CuiElement
                {
                    Name = LayerFeed + $".kill.{kill.Guid}" + ".target.div" + ".targettext",
                    Parent = LayerFeed + $".kill.{kill.Guid}" + ".target.div",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = string.Format(settings.TargetTextSettings.TextFormat,
                                settings.TargetTextSettings.Trim(GetIdealNameForFeed(kill.Target.Name, lang.GetLanguage(player.UserIDString)))),
                            Font = settings.TargetTextSettings.Font, FontSize = settings.TargetTextSettings.FontSize,
                            Align = settings.TargetTextSettings.GetAlign(), Color = settings.TargetTextSettings.Color,
                            FadeIn = kill.NeedFadeIn ? settings.FadeIn : 0f
                        },
                        new CuiOutlineComponent
                        {
                            Distance = settings.TargetTextSettings.OutlineDistance,
                            Color = settings.TargetTextSettings.OutlineColor
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = targetText.OffsetMin, OffsetMax = targetText.OffsetMax
                        }
                    }
                });

            if (conditions.EnableTargetAvatar && settings.Elements.TryGetValue("target_avatar", out var targetAvatar))
                UI_GetAvatar(ref container, settings, kill, kill.TargetAvatar,
                    LayerFeed + $".kill.{kill.Guid}" + ".target.div",
                    targetAvatar.OffsetMin, targetAvatar.OffsetMax,
                    LayerFeed + $".kill.{kill.Guid}" + ".target.div" + ".avatar");

            var weapon = kill.Weapon ?? new WeaponData();
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0" },
                RectTransform =
                {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = "-45.121 -12.408", OffsetMax = "45.121 12.408"
                }
            }, LayerFeed + $".kill.{kill.Guid}", LayerFeed + $".kill.{kill.Guid}" + ".weapon.div");

            var div = LayerFeed + $".kill.{kill.Guid}" + ".weapon.div";

            if (conditions.EnableWeapon)
            {
                if (weapon.IsItem)
                    container.Add(new CuiPanel
                    {
                        Image = { ItemId = weapon.ItemId, SkinId = weapon.SkinID, FadeIn = kill.NeedFadeIn ? settings.FadeIn : 0f },
                        RectTransform =
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = weaponIcon.OffsetMin, OffsetMax = weaponIcon.OffsetMax
                        }
                    }, div, div + ".weapon");
                else if (weapon.Image != null && weapon.Image.Type != ImageType.None &&
                         weapon.Image.Type != ImageType.NotNeed)
                    UI_GetKillImage(ref container, settings, kill, weapon.Image,
                        (weapon.Image is ColoredImage ci) ? ci.Color : "1 1 1 1",
                        div, weaponIcon.OffsetMin, weaponIcon.OffsetMax, div + ".weapon");
            }

            if (weapon.AmmoItemId != 0 && conditions.EnableAmmo)
                container.Add(new CuiPanel
                {
                    Image = { ItemId = weapon.AmmoItemId, FadeIn = kill.NeedFadeIn ? settings.FadeIn : 0f },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                        OffsetMin = ammoIcon.OffsetMin, OffsetMax = ammoIcon.OffsetMax
                    }
                }, div, div + ".ammo");

            if (kill.Distance > 0.03f && conditions.EnableDistance)
                container.Add(new CuiElement
                {
                    Name = div + ".distance" + ".1716",
                    Parent = div,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = string.Format(settings.DistanceTextSettings.TextFormat, kill.Distance),
                            Font = settings.DistanceTextSettings.Font, FontSize = settings.DistanceTextSettings.FontSize,
                            Align = settings.DistanceTextSettings.GetAlign(), Color = settings.DistanceTextSettings.Color,
                            FadeIn = kill.NeedFadeIn ? settings.FadeIn : 0f
                        },
                        new CuiOutlineComponent
                        {
                            Distance = settings.DistanceTextSettings.OutlineDistance,
                            Color = settings.DistanceTextSettings.OutlineColor
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = distanceText.OffsetMin, OffsetMax = distanceText.OffsetMax
                        }
                    }
                });

            if (kill.IsHeadshot && conditions.EnableHeadshot)
                UI_GetKillImage(ref container, settings, kill, settings.HeadshotImage, settings.HeadshotImage.Color,
                    div, headshotIcon.OffsetMin, headshotIcon.OffsetMax, div + ".headshot");
        }

        private void UI_GetKillImage(ref CuiElementContainer container, ConfigData.UISettings settings, KillData kill,
            Image image, string color, string parent, string offsetMin, string offsetMax, string name = null)
        {
            if (image?.Value == null)
                return;

            var fade = kill.NeedFadeIn ? settings.FadeIn : 0f;

            switch (image.Type)
            {
                case ImageType.Shortname:
                    container.Add(new CuiPanel
                    {
                        Image = { ItemId = ValidateItemID(image.Value), FadeIn = fade },
                        RectTransform =
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = offsetMin, OffsetMax = offsetMax
                        }
                    }, parent, name);
                    break;
                case ImageType.Sprite:
                    container.Add(new CuiPanel
                    {
                        Image = { Sprite = image.Value as string, Color = color ?? "1 1 1 1", FadeIn = fade },
                        RectTransform =
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = offsetMin, OffsetMax = offsetMax
                        }
                    }, parent, name);
                    break;
                case ImageType.URL:
                    var png = GetImage(image.Value as string);
                    if (string.IsNullOrEmpty(png))
                        break;
                    container.Add(new CuiElement
                    {
                        Name = name,
                        Parent = parent,
                        Components =
                        {
                            new CuiRawImageComponent { Png = png, Color = color ?? "1 1 1 1", FadeIn = fade },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                                OffsetMin = offsetMin, OffsetMax = offsetMax
                            }
                        }
                    });
                    break;
            }
        }

        private void UI_GetAvatar(ref CuiElementContainer container, ConfigData.UISettings settings, KillData kill,
            AvatarData avatar, string parent, string offsetMin, string offsetMax, string name)
        {
            if (avatar == null)
                return;

            var fade = kill.NeedFadeIn ? settings.FadeIn : 0f;

            if (avatar.UseSteamId)
            {
                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    Components =
                    {
                        new CuiRawImageComponent { SteamId = avatar.SteamId.ToString(), FadeIn = fade },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = offsetMin, OffsetMax = offsetMax
                        }
                    }
                });
                return;
            }

            UI_GetKillImage(ref container, settings, kill, avatar.Image,
                (avatar.Image is ColoredImage ci) ? ci.Color : "1 1 1 1",
                parent, offsetMin, offsetMax, name);
        }

        #endregion

        #region Commands

        [ConsoleCommand("kf.section")]
        private void cmdSection(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!player || !arg.HasArgs())
                return;

            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
                return;

            var sectionId = arg.GetString(0);
            _activeSection[player.userID] = sectionId;

            var container = new CuiElementContainer();
            UI_DrawNav(ref container, GetTheme(player), sectionId, player);
            CuiHelper.AddUi(player, container);

            UI_DrawSectionContent(player, sectionId);
        }

        [ConsoleCommand("kf.theme")]
        private void CmdTheme(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!player || !arg.HasArgs())
                return;

            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
                return;

            if (!Enum.TryParse(arg.GetString(0), out UITheme theme))
                return;

            if (!_killfeedPreforms.ContainsKey(player))
                _killfeedPreforms.Add(player, MakePreform(player));

            _killfeedPreforms[player].EditorTheme = theme;
            _adminThemes[player.userID] = theme;
            SaveData();

            UI_DrawMain(player);
        }

        [ConsoleCommand("kf.reset")]
        private void CmdReset(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!player)
                return;

            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
                return;

            _killfeedPreforms.Remove(player);
            _killfeedPreforms.Add(player, MakePreform(player));

            UI_DrawMain(player);
            _feedBehaviour.PlayerUpdate(player);
        }

        [ChatCommand("kf.edit")]
        private void CmdEdit(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
                return;

            if (args.Length <= 0 || !bool.TryParse(args[0], out var isReturn) || !isReturn)
            {
                _killfeedPreforms.Remove(player);
                _killfeedPreforms.Add(player, MakePreform(player));
            }

            UI_DrawMain(player);
            _feedBehaviour.PlayerUpdate(player);
        }

        [ConsoleCommand("kf.confirmpreform")]
        private void CmdConfirmPreform(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                return;
            var player = arg.Player();
            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
                return;

            if (!_killfeedPreforms.TryGetValue(player, out var data))
                return;

            cfg = data;
            SaveConfig(cfg);
            _killfeedPreforms.Remove(player);
            _activeSection.Remove(player.userID);
            CuiHelper.DestroyUi(player, Layer);
            CuiHelper.DestroyUi(player, LayerFeed);

            foreach (var x in BasePlayer.activePlayerList)
                if (!_killfeedPreforms.ContainsKey(x))
                    _feedBehaviour.PlayerUpdate(x);
        }

        [ConsoleCommand("kf.cancelpreform")]
        private void CmdCancelPreform(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                return;
            var player = arg.Player();
            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
                return;

            _killfeedPreforms.Remove(player);
            _activeSection.Remove(player.userID);
            CuiHelper.DestroyUi(player, Layer);
            CuiHelper.DestroyUi(player, LayerFeed);
            _feedBehaviour.PlayerUpdate(player);
        }

        [ConsoleCommand("kf.setvalue")]
        private void CmdChangeIndent(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null || !permission.UserHasPermission(arg.Player().UserIDString, PermAdmin))
                return;

            var player = arg.Player();
            if (!_killfeedPreforms.TryGetValue(player, out var killfeedPrefab))
                return;

            var key = arg.GetString(1);
            var value = ConcatArgs(arg.Args.Select(x => x.ToString()).ToArray(), 2);
            var needUpdate = arg.GetBool(0);
            var uiSettings = killfeedPrefab.uISettings;

            try
            {
                HandleSettingChange(player, uiSettings, key, value, arg);
            }
            catch (Exception ex)
            {
                Puts($"Error handling setting change: {ex}");
                return;
            }

            if (needUpdate)
            {

                var elementEdit = key == "element.move" || key == "element.adjust" ||
                                  key == "element.x" || key == "element.y" ||
                                  key == "element.width" || key == "element.height" ||
                                  key == "element.move.step";

                if (elementEdit)
                    UI_RefreshElementEditor(player);
                else if (key == "selection.update")
                    UI_RefreshElements(player);
                else if (key == "monument_toggle" || key == "zm_flag")
                    UI_RefreshFilters(player);
                else
                    UI_DrawSectionContent(player, GetActiveSection(player));

                _feedBehaviour.PlayerUpdate(player);
            }
        }

        private void HandleSettingChange(BasePlayer player, ConfigData.UISettings uiSettings, string key, string value,
            ConsoleSystem.Arg arg)
        {
            switch (key)
            {
                case "toggle":
                    HandleToggle(uiSettings.Conditions, value);
                    break;

                case "toggle.cfg":
                    HandleConfigToggle(player, value);
                    break;

                case "toggle.ui":
                    HandleUiToggle(uiSettings, value);
                    break;

                case "direction":
                    uiSettings.Conditions.FromUpToDown = value == "down";
                    break;

                case "selection.update":
                    if (uiSettings.Elements.ContainsKey(value))
                        _selectedElement[player.userID] = value;
                    break;

                case "element.move.step":
                    _moveStep[player.userID] = ParseFloat(value);
                    break;

                case "element.move":

                    if (uiSettings.Elements.TryGetValue(arg.GetString(2), out var elemMove))
                        elemMove.Move(arg.GetString(3), arg.GetFloat(4));
                    break;

                case "element.x":
                    if (uiSettings.Elements.TryGetValue(arg.GetString(2), out var elemX))
                    {
                        elemX.X = arg.GetFloat(3);
                        elemX.MarkDirty();
                    }
                    break;

                case "element.y":
                    if (uiSettings.Elements.TryGetValue(arg.GetString(2), out var elemY))
                    {
                        elemY.Y = arg.GetFloat(3);
                        elemY.MarkDirty();
                    }
                    break;

                case "element.width":
                case "element.height":
                    HandleElementDimension(uiSettings, key, arg.GetString(2), arg.GetString(3));
                    break;

                case "element.adjust":
                    if (uiSettings.Elements.TryGetValue(arg.GetString(2), out var elemAdj))
                    {
                        var delta = arg.GetFloat(4);
                        switch (arg.GetString(3))
                        {
                            case "x": elemAdj.X += delta; break;
                            case "y": elemAdj.Y += delta; break;
                            case "width": elemAdj.Width = Mathf.Max(0f, elemAdj.Width + delta); break;
                            case "height": elemAdj.Height = Mathf.Max(0f, elemAdj.Height + delta); break;
                        }

                        elemAdj.MarkDirty();
                    }
                    break;

                case "killer_maxnick":
                    uiSettings.KillerTextSettings.MaxNickLength = ParseInt(value);
                    break;

                case "target_maxnick":
                    uiSettings.TargetTextSettings.MaxNickLength = ParseInt(value);
                    break;

                case "fadein":
                    uiSettings.FadeIn = ParseFloat(value);
                    break;
                case "lifetime":
                    _killfeedPreforms[player].Lifetime = ParseInt(value);
                    break;

                case "anim_distance":
                    uiSettings.AnimationDistance = ParseFloat(value);
                    break;
                case "anim_duration":
                    uiSettings.AnimationDuration = ParseFloat(value);
                    break;
                case "anim_indir":
                    uiSettings.AnimationInDirection = value;
                    break;
                case "anim_outdir":
                    uiSettings.AnimationOutDirection = value;
                    break;

                case "deathdefault_icon_color":
                    _killfeedPreforms[player].DefaultDeathImage.Color = NormalizeRgba(value);
                    HandleDeathImage(player, value, true);
                    break;

                case "deathimage":
                    HandleDeathImage(player, value, false);
                    break;

                case "bgcolor":
                    uiSettings.BGColor = NormalizeRgba(value);
                    break;

                case "nokiller_format":
                    uiSettings.NoKillerTextFormat = string.IsNullOrEmpty(value) ? "{0} | {1}" : value;
                    break;

                case "zm_flag":
                    _killfeedPreforms[player].ZoneManagerFlag = value;
                    break;

                case "monument_toggle":
                    var blacklist = _killfeedPreforms[player].MonumentBlacklist ??= new();
                    if (blacklist.RemoveAll(b => string.Equals(b, value, StringComparison.OrdinalIgnoreCase)) == 0)
                        blacklist.Add(value);
                    break;

                case "maxkills":
                    uiSettings.MaxPanels = ParseInt(value);
                    break;

                case "minx":
                case "maxx":
                case "miny":
                case "maxy":
                case "indent":
                    HandleOffsetSettings(uiSettings.OffsetSettings, key, value);
                    break;

                case "killer_text_align":
                case "killer_text":
                case "killer_fontsize":
                case "killer_font":
                case "killer_color":
                case "killer_outlinedistance":
                case "killer_outlinecolor":
                    HandleTextSettings(uiSettings.KillerTextSettings, key, value, player, "killer");
                    break;

                case "target_text_align":
                case "target_text":
                case "target_fontsize":
                case "target_font":
                case "target_color":
                case "target_outlinedistance":
                case "target_outlinecolor":
                    HandleTextSettings(uiSettings.TargetTextSettings, key, value, player, "target");
                    break;

                case "distance_text_align":
                case "distance_text":
                case "distance_outlinedistance":
                case "distance_outlinecolor":
                case "distance_fontsize":
                case "distance_font":
                case "distance_color":
                    HandleTextSettings(uiSettings.DistanceTextSettings, key, value, player, "distance");
                    break;

                case "set_anchor":
                    HandleAnchorSetting(uiSettings, value);
                    break;

                case "headshot_icon_color":
                    uiSettings.HeadshotImage.Color = NormalizeRgba(value);
                    HandleHeadshotImage(player, uiSettings, value, true);
                    break;

                case "headshotimage":
                    HandleHeadshotImage(player, uiSettings, value, false);
                    break;
            }
        }

        #region Helper Methods

        private void HandleToggle(ConfigData.ElementConditions conditions, string field)
        {
            switch (field)
            {
                case "showKiller":
                    conditions.EnableKillerNickname = !conditions.EnableKillerNickname;
                    break;
                case "showTarget":
                    conditions.EnableTargetNickname = !conditions.EnableTargetNickname;
                    break;
                case "showWeapon":
                    conditions.EnableWeapon = !conditions.EnableWeapon;
                    break;
                case "showAmmo":
                    conditions.EnableAmmo = !conditions.EnableAmmo;
                    break;
                case "showDistance":
                    conditions.EnableDistance = !conditions.EnableDistance;
                    break;
                case "showHeadshot":
                    conditions.EnableHeadshot = !conditions.EnableHeadshot;
                    break;
            }
        }

        private void HandleConfigToggle(BasePlayer player, string field)
        {
            switch (field)
            {
                case "showPrivate":
                    _killfeedPreforms[player].PrivateKillsOnly = !_killfeedPreforms[player].PrivateKillsOnly;
                    break;
            }
        }

        private void HandleUiToggle(ConfigData.UISettings uiSettings, string field)
        {
            switch (field)
            {
                case "animate":
                    uiSettings.AnimateFeed = !uiSettings.AnimateFeed;
                    break;
                case "animate_exit":
                    uiSettings.AnimateFeedExit = !uiSettings.AnimateFeedExit;
                    break;
            }
        }

        private void HandleElementDimension(ConfigData.UISettings uiSettings, string key, string elementKey,
            string dimensionValue)
        {
            if (!uiSettings.Elements.TryGetValue(elementKey, out var element))
                return;

            var dimension = ParseFloat(dimensionValue);

            if (key == "element.width")
                element.Width = dimension;
            else
                element.Height = dimension;

            element.MarkDirty();
        }

        private void HandleDeathImage(BasePlayer player, string value, bool justUpdate)
        {
            var killfeedPrefab = _killfeedPreforms[player];
            var type = GetImageType(killfeedPrefab.DefaultDeathImage.Value as string);

            if (!justUpdate)
            {
                killfeedPrefab.DefaultDeathImage.Value = value;
                killfeedPrefab.DefaultDeathImage.Type = type;
            }

            if (type == ImageType.URL)
            {

            }
            else
            {

            }
        }

        private void HandleOffsetSettings(OffsetSettings offsetSettings, string key, string value)
        {
            switch (key)
            {
                case "minx":
                    offsetSettings.OffsetMinX = ParseFloat(value);
                    break;
                case "maxx":
                    offsetSettings.OffsetMaxX = ParseFloat(value);
                    break;
                case "miny":
                    offsetSettings.OffsetMinY = ParseFloat(value);
                    break;
                case "maxy":
                    offsetSettings.OffsetMaxY = ParseFloat(value);
                    break;
                case "indent":
                    offsetSettings.Indent = ParseFloat(value);
                    break;
            }
        }

        private void HandleTextSettings(ConfigData.TextSettings textSettings, string key, string value,
            BasePlayer player, string textType)
        {

            var prefix = textType + "_";
            if (!key.StartsWith(prefix))
                return;

            var field = key.Substring(prefix.Length);

            switch (field)
            {
                case "text":
                    textSettings.TextFormat = value;
                    break;
                case "fontsize":
                    textSettings.FontSize = ParseInt(value);
                    break;
                case "font":
                    textSettings.Font = value;
                    break;
                case "color":
                    textSettings.Color = NormalizeRgba(value);

                    break;
                case "outlinedistance":
                    textSettings.OutlineDistance = value;
                    break;
                case "outlinecolor":
                    textSettings.OutlineColor = NormalizeRgba(value);
                    break;
                case "text_align":
                    textSettings.Alignment = value;
                    break;
            }
        }

        private void HandleAnchorSetting(ConfigData.UISettings uiSettings, string value)
        {
            if (!Enum.TryParse(value, out AnchorType anchorType) ||
                !_anchors.TryGetValue(anchorType, out var kvAnchors))
                return;

            uiSettings.AnchorSettings.AnchorMin = kvAnchors.Key;
            uiSettings.AnchorSettings.AnchorMax = kvAnchors.Value;
        }

        private void HandleHeadshotImage(BasePlayer player, ConfigData.UISettings uiSettings, string value,
            bool justUpdate)
        {
            var type = GetImageType(uiSettings.HeadshotImage.Value as string);

            if (!justUpdate)
            {
                uiSettings.HeadshotImage.Value = value;
                uiSettings.HeadshotImage.Type = type;
            }

            if (type == ImageType.URL)
            {

            }
            else
            {

            }
        }

        #endregion

        [ChatCommand("killfeed")]
        private void CmdSwitchVisibility(BasePlayer player)
        {
            if (_disabledFeed.Contains(player.userID))
            {
                _disabledFeed.Remove(player.userID);
                _feedBehaviour.PlayerUpdate(player);
                player.ChatMessage("You <color=green>enabled</color> killfeed");
                return;
            }

            _disabledFeed.Add(player.userID);
            player.ChatMessage("You <color=green>disabled</color> killfeed");
            CuiHelper.DestroyUi(player, LayerFeed);
        }

        #endregion

        #region Lang

        private string GetLocalizedMessage(string key, BasePlayer player) =>
            lang.GetMessage(key, this, player?.UserIDString);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["nav.cat.base"] = "БАЗОВОЕ",
                ["nav.cat.text"] = "ТЕКСТ",
                ["nav.cat.graphics"] = "ГРАФИКА",
                ["nav.cat.extra"] = "ДОПОЛНИТЕЛЬНО",
                ["nav.general"] = "Основное",
                ["nav.layout"] = "Положение и фон",
                ["nav.killer"] = "Убийца",
                ["nav.target"] = "Жертва",
                ["nav.distance"] = "Дистанция",
                ["nav.icons"] = "Иконки",
                ["nav.anim"] = "Анимация",
                ["nav.themes"] = "Темы",

                ["nav.filters"] = "Фильтры",
                ["grp.zonemanager"] = "ZoneManager",
                ["lbl.zm_flag"] = "Флаг зоны (скрывать киллы)",
                ["zm.loaded"] = "ZoneManager подключён",
                ["zm.missing"] = "ZoneManager не установлен",
                ["grp.monuments"] = "Монументы (скрыть киллы внутри)",
                ["mon.missing"] = "MonumentFinder не установлен",
                ["mon.none"] = "Монументы не найдены",
                ["mon.hidden"] = "Скрыт",
                ["mon.shown"] = "Показан",

                ["sec.general.title"] = "ОСНОВНЫЕ НАСТРОЙКИ",
                ["sec.general.sub"] = "Что показывать в полоске убийств",
                ["sec.layout.title"] = "ПОЛОЖЕНИЕ И ФОН",
                ["sec.layout.sub"] = "Якорь, смещение, фон сообщений",
                ["sec.killer.title"] = "НАСТРОЙКИ УБИЙЦЫ",
                ["sec.killer.sub"] = "Внешний вид никнейма стрелявшего",
                ["sec.target.title"] = "НАСТРОЙКИ ЖЕРТВЫ",
                ["sec.target.sub"] = "Внешний вид никнейма цели",
                ["sec.distance.title"] = "НАСТРОЙКИ ДИСТАНЦИИ",
                ["sec.distance.sub"] = "Текст с расстоянием между игроками",
                ["sec.icons.title"] = "ИКОНКИ",
                ["sec.icons.sub"] = "Путь к ассету или URL картинки",
                ["sec.anim.title"] = "АНИМАЦИЯ",
                ["sec.anim.sub"] = "Плавность появления и исчезновения",
                ["sec.themes.title"] = "ТЕМЫ ОФОРМЛЕНИЯ",
                ["sec.themes.sub"] = "Применяется к панели и предпросмотру",

                ["grp.visibility"] = "Видимость элементов",
                ["grp.behaviour"] = "Поведение ленты",
                ["grp.anchor"] = "Якорь и смещение",
                ["grp.bg"] = "Фон сообщений",
                ["grp.format"] = "Формат текста",
                ["grp.color"] = "Цвет и обводка",
                ["grp.hs_icon"] = "Иконка хедшота",
                ["grp.death_icon"] = "Иконка по умолчанию",
                ["grp.timings"] = "Тайминги",
                ["grp.presets"] = "Пресеты оформления",

                ["tg.killer"] = "Ник убийцы",
                ["tg.killer.d"] = "Имя стрелявшего",
                ["tg.target"] = "Ник жертвы",
                ["tg.target.d"] = "Имя цели",
                ["tg.weapon"] = "Иконка оружия",
                ["tg.weapon.d"] = "Картинка оружия",
                ["tg.ammo"] = "Иконка патрона",
                ["tg.ammo.d"] = "Тип боеприпаса",
                ["tg.distance"] = "Дистанция",
                ["tg.distance.d"] = "Расстояние",
                ["tg.headshot"] = "Хедшот",
                ["tg.headshot.d"] = "Метка выстрела в голову",
                ["tg.private"] = "Приватные киллы",
                ["tg.private.d"] = "Показывать килл только убийце и жертве",
                ["tg.animate"] = "Анимация ленты",
                ["tg.animate.d"] = "Новые киллы выезжают справа",
                ["tg.animate_exit"] = "Анимация исчезновения",
                ["tg.animate_exit.d"] = "Устаревшие киллы выезжают за край вместо мгновенного исчезновения",
                ["lbl.anim_indir"] = "Появление со стороны",
                ["lbl.anim_outdir"] = "Исчезновение в сторону",
                ["lbl.anim_dist"] = "Дистанция выезда (px)",
                ["lbl.anim_dur"] = "Длительность (сек)",
                ["opt.off"] = "Выкл",
                ["opt.dir_right"] = "Справа",
                ["opt.dir_left"] = "Слева",

                ["lbl.direction"] = "Направление",
                ["lbl.dir.down"] = "Сверху вниз",
                ["lbl.dir.up"] = "Снизу вверх",
                ["lbl.maxkills"] = "Макс. сообщений",
                ["lbl.offx"] = "Смещение X",
                ["lbl.offy"] = "Смещение Y",
                ["lbl.indent"] = "Отступ строк",
                ["lbl.anchor"] = "ТОЧКА ПРИВЯЗКИ",
                ["lbl.bgcolor"] = "Цвет фона (R G B A)",
                ["lbl.tpl.nick"] = "Шаблон  ({0} = ник)",
                ["lbl.nokiller_fmt"] = "Формат  ({0} = жертва, {1} = причина)",
                ["lbl.tpl.meters"] = "Шаблон  ({0} = метры)",
                ["lbl.align"] = "Выравнивание",
                ["lbl.size"] = "Размер",
                ["lbl.text_color"] = "Цвет текста (R G B A)",
                ["lbl.outline_color"] = "Цвет обводки (R G B A)",
                ["lbl.outline_dist"] = "Толщина обводки (X Y)",
                ["lbl.path_url"] = "Путь / URL",
                ["lbl.tint"] = "Цвет (tint) (R G B A)",
                ["lbl.fadein"] = "Fade In (сек)",
                ["lbl.lifetime"] = "Lifetime (сек)",

                ["btn.reset"] = "Сбросить",
                ["btn.save"] = "Сохранить",

                ["theme.rust"] = "Rust Classic",
                ["theme.rust.d"] = "Тёплые тона, оранжевый акцент",
                ["theme.midnight"] = "Midnight",
                ["theme.midnight.d"] = "Тёмно-синяя гамма, голубой акцент",
                ["theme.neon"] = "Neon",
                ["theme.neon.d"] = "Кибер-стиль, фиолетовый и розовый",

                ["nav.elements"] = "Элементы",
                ["sec.elements.title"] = "ПОЗИЦИИ ЭЛЕМЕНТОВ",
                ["sec.elements.sub"] = "Положение и размер каждого элемента ленты",
                ["sec.filters.title"] = "ФИЛЬТРЫ",
                ["sec.filters.sub"] = "Где скрывать убийства: зоны и монументы",
                ["grp.el_list"] = "Список элементов",
                ["grp.el_edit"] = "Редактор",
                ["el.killer_text"] = "Ник убийцы",
                ["el.killer_avatar"] = "Аватар убийцы",
                ["el.target_text"] = "Ник жертвы",
                ["el.target_avatar"] = "Аватар жертвы",
                ["el.weapon_icon"] = "Иконка оружия",
                ["el.ammo_icon"] = "Иконка патрона",
                ["el.distance_text"] = "Дистанция",
                ["el.headshot_icon"] = "Хедшот",
                ["el.nokiller_text"] = "Текст (без убийцы)",
                ["el.nokiller_icon"] = "Иконка (без убийцы)",
                ["lbl.pos_x"] = "Позиция X",
                ["lbl.pos_y"] = "Позиция Y",
                ["lbl.width"] = "Ширина",
                ["lbl.height"] = "Высота",
                ["lbl.move_step"] = "Шаг",
                ["lbl.move"] = "Перемещение",
                ["lbl.maxnick"] = "Макс. символов в нике"
            }, this, "ru");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["nav.cat.base"] = "BASIC",
                ["nav.cat.text"] = "TEXT",
                ["nav.cat.graphics"] = "GRAPHICS",
                ["nav.cat.extra"] = "EXTRA",
                ["nav.general"] = "General",
                ["nav.layout"] = "Position & background",
                ["nav.killer"] = "Killer",
                ["nav.target"] = "Victim",
                ["nav.distance"] = "Distance",
                ["nav.icons"] = "Icons",
                ["nav.anim"] = "Animation",
                ["nav.themes"] = "Themes",

                ["nav.filters"] = "Filters",
                ["grp.zonemanager"] = "ZoneManager",
                ["lbl.zm_flag"] = "Zone flag (hide kills)",
                ["zm.loaded"] = "ZoneManager connected",
                ["zm.missing"] = "ZoneManager not installed",
                ["grp.monuments"] = "Monuments (hide kills inside)",
                ["mon.missing"] = "MonumentFinder not installed",
                ["mon.none"] = "No monuments found",
                ["mon.hidden"] = "Hidden",
                ["mon.shown"] = "Shown",

                ["sec.general.title"] = "GENERAL SETTINGS",
                ["sec.general.sub"] = "What to show in the kill feed",
                ["sec.layout.title"] = "POSITION & BACKGROUND",
                ["sec.layout.sub"] = "Anchor, offset, message background",
                ["sec.killer.title"] = "KILLER SETTINGS",
                ["sec.killer.sub"] = "Appearance of the killer's nickname",
                ["sec.target.title"] = "VICTIM SETTINGS",
                ["sec.target.sub"] = "Appearance of the victim's nickname",
                ["sec.distance.title"] = "DISTANCE SETTINGS",
                ["sec.distance.sub"] = "Distance text between players",
                ["sec.icons.title"] = "ICONS",
                ["sec.icons.sub"] = "Asset path or image URL",
                ["sec.anim.title"] = "ANIMATION",
                ["sec.anim.sub"] = "Fade in / out smoothness",
                ["sec.themes.title"] = "THEMES",
                ["sec.themes.sub"] = "Applies to the panel and preview",

                ["grp.visibility"] = "Element visibility",
                ["grp.behaviour"] = "Feed behaviour",
                ["grp.anchor"] = "Anchor & offset",
                ["grp.bg"] = "Message background",
                ["grp.format"] = "Text format",
                ["grp.color"] = "Color & outline",
                ["grp.hs_icon"] = "Headshot icon",
                ["grp.death_icon"] = "Default icon",
                ["grp.timings"] = "Timings",
                ["grp.presets"] = "Theme presets",

                ["tg.killer"] = "Killer name",
                ["tg.killer.d"] = "The shooter's name",
                ["tg.target"] = "Victim name",
                ["tg.target.d"] = "The target's name",
                ["tg.weapon"] = "Weapon icon",
                ["tg.weapon.d"] = "Weapon image",
                ["tg.ammo"] = "Ammo icon",
                ["tg.ammo.d"] = "Ammo type",
                ["tg.distance"] = "Distance",
                ["tg.distance.d"] = "Distance",
                ["tg.headshot"] = "Headshot",
                ["tg.headshot.d"] = "Headshot marker",
                ["tg.private"] = "Private kills",
                ["tg.private.d"] = "Show each kill only to the killer and the victim",
                ["tg.animate"] = "Feed animation",
                ["tg.animate.d"] = "Slide new kills in from the right",
                ["tg.animate_exit"] = "Exit animation",
                ["tg.animate_exit.d"] = "Slide expiring kills out instead of vanishing",
                ["lbl.anim_indir"] = "Entrance from",
                ["lbl.anim_outdir"] = "Exit to",
                ["lbl.anim_dist"] = "Slide distance (px)",
                ["lbl.anim_dur"] = "Duration (sec)",
                ["opt.off"] = "Off",
                ["opt.dir_right"] = "Right",
                ["opt.dir_left"] = "Left",

                ["lbl.direction"] = "Direction",
                ["lbl.dir.down"] = "Top to bottom",
                ["lbl.dir.up"] = "Bottom to top",
                ["lbl.maxkills"] = "Max messages",
                ["lbl.offx"] = "Offset X",
                ["lbl.offy"] = "Offset Y",
                ["lbl.indent"] = "Row spacing",
                ["lbl.anchor"] = "ANCHOR POINT",
                ["lbl.bgcolor"] = "Background color (R G B A)",
                ["lbl.tpl.nick"] = "Template  ({0} = name)",
                ["lbl.nokiller_fmt"] = "Format  ({0} = victim, {1} = reason)",
                ["lbl.tpl.meters"] = "Template  ({0} = meters)",
                ["lbl.align"] = "Alignment",
                ["lbl.size"] = "Size",
                ["lbl.text_color"] = "Text color (R G B A)",
                ["lbl.outline_color"] = "Outline color (R G B A)",
                ["lbl.outline_dist"] = "Outline thickness (X Y)",
                ["lbl.path_url"] = "Path / URL",
                ["lbl.tint"] = "Tint color (R G B A)",
                ["lbl.fadein"] = "Fade In (sec)",
                ["lbl.lifetime"] = "Lifetime (sec)",

                ["btn.reset"] = "Reset",
                ["btn.save"] = "Save",

                ["theme.rust"] = "Rust Classic",
                ["theme.rust.d"] = "Warm tones, orange accent",
                ["theme.midnight"] = "Midnight",
                ["theme.midnight.d"] = "Dark blue palette, cyan accent",
                ["theme.neon"] = "Neon",
                ["theme.neon.d"] = "Cyber style, purple and pink",

                ["nav.elements"] = "Elements",
                ["sec.elements.title"] = "ELEMENT POSITIONS",
                ["sec.elements.sub"] = "Position and size of each feed element",
                ["sec.filters.title"] = "FILTERS",
                ["sec.filters.sub"] = "Where to hide kills: zones and monuments",
                ["grp.el_list"] = "Element list",
                ["grp.el_edit"] = "Editor",
                ["el.killer_text"] = "Killer name",
                ["el.killer_avatar"] = "Killer avatar",
                ["el.target_text"] = "Victim name",
                ["el.target_avatar"] = "Victim avatar",
                ["el.weapon_icon"] = "Weapon icon",
                ["el.ammo_icon"] = "Ammo icon",
                ["el.distance_text"] = "Distance",
                ["el.headshot_icon"] = "Headshot",
                ["el.nokiller_text"] = "Text (no killer)",
                ["el.nokiller_icon"] = "Icon (no killer)",
                ["lbl.pos_x"] = "Position X",
                ["lbl.pos_y"] = "Position Y",
                ["lbl.width"] = "Width",
                ["lbl.height"] = "Height",
                ["lbl.move_step"] = "Step",
                ["lbl.move"] = "Move",
                ["lbl.maxnick"] = "Max nickname chars"
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["nav.cat.base"] = "BASIS",
                ["nav.cat.text"] = "TEXT",
                ["nav.cat.graphics"] = "GRAFIK",
                ["nav.cat.extra"] = "EXTRA",
                ["nav.general"] = "Allgemein",
                ["nav.layout"] = "Position & Hintergrund",
                ["nav.killer"] = "Töter",
                ["nav.target"] = "Opfer",
                ["nav.distance"] = "Distanz",
                ["nav.icons"] = "Symbole",
                ["nav.anim"] = "Animation",
                ["nav.themes"] = "Themes",

                ["nav.filters"] = "Filter",
                ["grp.zonemanager"] = "ZoneManager",
                ["lbl.zm_flag"] = "Zonen-Flag (Kills verbergen)",
                ["zm.loaded"] = "ZoneManager verbunden",
                ["zm.missing"] = "ZoneManager nicht installiert",
                ["grp.monuments"] = "Monumente (Kills darin verbergen)",
                ["mon.missing"] = "MonumentFinder nicht installiert",
                ["mon.none"] = "Keine Monumente gefunden",
                ["mon.hidden"] = "Verborgen",
                ["mon.shown"] = "Sichtbar",

                ["sec.general.title"] = "ALLGEMEINE EINSTELLUNGEN",
                ["sec.general.sub"] = "Was im Kill-Feed angezeigt wird",
                ["sec.layout.title"] = "POSITION & HINTERGRUND",
                ["sec.layout.sub"] = "Anker, Versatz, Nachrichtenhintergrund",
                ["sec.killer.title"] = "TÖTER-EINSTELLUNGEN",
                ["sec.killer.sub"] = "Aussehen des Töternamens",
                ["sec.target.title"] = "OPFER-EINSTELLUNGEN",
                ["sec.target.sub"] = "Aussehen des Opfernamens",
                ["sec.distance.title"] = "DISTANZ-EINSTELLUNGEN",
                ["sec.distance.sub"] = "Distanztext zwischen Spielern",
                ["sec.icons.title"] = "SYMBOLE",
                ["sec.icons.sub"] = "Asset-Pfad oder Bild-URL",
                ["sec.anim.title"] = "ANIMATION",
                ["sec.anim.sub"] = "Ein-/Ausblenden",
                ["sec.themes.title"] = "THEMES",
                ["sec.themes.sub"] = "Gilt für Panel und Vorschau",

                ["grp.visibility"] = "Elementsichtbarkeit",
                ["grp.behaviour"] = "Feed-Verhalten",
                ["grp.anchor"] = "Anker & Versatz",
                ["grp.bg"] = "Nachrichtenhintergrund",
                ["grp.format"] = "Textformat",
                ["grp.color"] = "Farbe & Umriss",
                ["grp.hs_icon"] = "Kopfschuss-Symbol",
                ["grp.death_icon"] = "Standardsymbol",
                ["grp.timings"] = "Timings",
                ["grp.presets"] = "Theme-Vorlagen",

                ["tg.killer"] = "Töter-Name",
                ["tg.killer.d"] = "Name des Schützen",
                ["tg.target"] = "Opfer-Name",
                ["tg.target.d"] = "Name des Ziels",
                ["tg.weapon"] = "Waffensymbol",
                ["tg.weapon.d"] = "Waffenbild",
                ["tg.ammo"] = "Munitionssymbol",
                ["tg.ammo.d"] = "Munitionstyp",
                ["tg.distance"] = "Distanz",
                ["tg.distance.d"] = "Entfernung",
                ["tg.headshot"] = "Kopfschuss",
                ["tg.headshot.d"] = "Kopfschuss-Markierung",
                ["tg.private"] = "Private Kills",
                ["tg.private.d"] = "Zeige jeden Kill nur dem Toter und dem Opfer",
                ["tg.animate"] = "Feed-Animation",
                ["tg.animate.d"] = "Neue Kills gleiten von rechts herein",
                ["tg.animate_exit"] = "Ausblend-Animation",
                ["tg.animate_exit.d"] = "Ablaufende Kills gleiten hinaus statt zu verschwinden",
                ["lbl.anim_indir"] = "Eintritt von",
                ["lbl.anim_outdir"] = "Austritt nach",
                ["lbl.anim_dist"] = "Gleitdistanz (px)",
                ["lbl.anim_dur"] = "Dauer (Sek.)",
                ["opt.off"] = "Aus",
                ["opt.dir_right"] = "Rechts",
                ["opt.dir_left"] = "Links",

                ["lbl.direction"] = "Richtung",
                ["lbl.dir.down"] = "Oben nach unten",
                ["lbl.dir.up"] = "Unten nach oben",
                ["lbl.maxkills"] = "Max. Nachrichten",
                ["lbl.offx"] = "Versatz X",
                ["lbl.offy"] = "Versatz Y",
                ["lbl.indent"] = "Zeilenabstand",
                ["lbl.anchor"] = "ANKERPUNKT",
                ["lbl.bgcolor"] = "Hintergrundfarbe (R G B A)",
                ["lbl.tpl.nick"] = "Vorlage  ({0} = Name)",
                ["lbl.nokiller_fmt"] = "Format  ({0} = Opfer, {1} = Grund)",
                ["lbl.tpl.meters"] = "Vorlage  ({0} = Meter)",
                ["lbl.align"] = "Ausrichtung",
                ["lbl.size"] = "Größe",
                ["lbl.text_color"] = "Textfarbe (R G B A)",
                ["lbl.outline_color"] = "Umrissfarbe (R G B A)",
                ["lbl.outline_dist"] = "Umrissdicke (X Y)",
                ["lbl.path_url"] = "Pfad / URL",
                ["lbl.tint"] = "Tönung (R G B A)",
                ["lbl.fadein"] = "Fade In (Sek)",
                ["lbl.lifetime"] = "Lifetime (Sek)",

                ["btn.reset"] = "Zurücksetzen",
                ["btn.save"] = "Speichern",

                ["theme.rust"] = "Rust Classic",
                ["theme.rust.d"] = "Warme Töne, oranger Akzent",
                ["theme.midnight"] = "Midnight",
                ["theme.midnight.d"] = "Dunkelblaue Palette, Cyan-Akzent",
                ["theme.neon"] = "Neon",
                ["theme.neon.d"] = "Cyber-Stil, Lila und Pink",

                ["nav.elements"] = "Elemente",
                ["sec.elements.title"] = "ELEMENTPOSITIONEN",
                ["sec.elements.sub"] = "Position und Größe jedes Feed-Elements",
                ["sec.filters.title"] = "FILTER",
                ["sec.filters.sub"] = "Wo Kills verborgen werden: Zonen und Monumente",
                ["grp.el_list"] = "Elementliste",
                ["grp.el_edit"] = "Editor",
                ["el.killer_text"] = "Töter-Name",
                ["el.killer_avatar"] = "Avatar des Töters",
                ["el.target_text"] = "Opfer-Name",
                ["el.target_avatar"] = "Avatar des Opfers",
                ["el.weapon_icon"] = "Waffensymbol",
                ["el.ammo_icon"] = "Munitionssymbol",
                ["el.distance_text"] = "Distanz",
                ["el.headshot_icon"] = "Kopfschuss",
                ["el.nokiller_text"] = "Text (ohne Töter)",
                ["el.nokiller_icon"] = "Symbol (ohne Töter)",
                ["lbl.pos_x"] = "Position X",
                ["lbl.pos_y"] = "Position Y",
                ["lbl.width"] = "Breite",
                ["lbl.height"] = "Höhe",
                ["lbl.move_step"] = "Schritt",
                ["lbl.move"] = "Bewegen",
                ["lbl.maxnick"] = "Max. Namenslänge"
            }, this, "de");
        }

        #endregion

        #region Config

        private ConfigData cfg;

        internal class ConfigData
        {

            [JsonIgnore] public UITheme EditorTheme = UITheme.Rust;

            [JsonConverter(typeof(StringEnumConverter))]
            [JsonProperty("Which method of download images use? (FileManager)")]
            public ImageLoadingType ImageLoadingType;

            [JsonProperty("Add bots deaths to killfeed?", Order = 0)]
            public bool AddBots;

            [JsonProperty("Kill lifetime in UI", Order = 1)]
            public int Lifetime;

            [JsonProperty("Default death image (types - Sprite, URL, Shortname)", Order = 2)]
            public ColoredImage DefaultDeathImage;

            [JsonProperty("UI Settings", Order = 4)]
            public UISettings uISettings;

            [JsonProperty("Entity -> Image (U can enter URL, Sprite and item shortname)", Order = 5)]
            public Dictionary<string, string> EntityToImage;

            [JsonProperty("Entity -> Name", Order = 6)]
            public Dictionary<string, Dictionary<string, string>> EntityToName;

            [JsonProperty("Add animals deaths to killfeed?", Order = 0)]
            public bool AddAnimals;

            [JsonProperty("Add BradleyAPC deaths to killfeed?", Order = 0)]
            public bool AddBradley;

            [JsonProperty("Add patrol helicopter deaths to killfeed?", Order = 0)]
            public bool AddPatrolHeli;

            [JsonProperty("Show kills only to killer and victim (private mode)", Order = 3)]
            public bool PrivateKillsOnly;

            [JsonProperty("ZoneManager: hide kills when killer or victim is in a zone with this flag (Custom1-Custom5, empty = off)", Order = 7)]
            public string ZoneManagerFlag = "Custom1";

            [JsonProperty("MonumentFinder: hide kills inside these monuments (short names)", Order = 8)]
            public List<string> MonumentBlacklist = new();

            [JsonProperty("Version", Order = 100)] public VersionNumber Version;

            internal class ElementData
            {
                [JsonIgnore] public Dictionary<(int, float, bool), string> cachedPositionsMax = new();
                [JsonIgnore] public Dictionary<(int, float, bool), string> cachedPositionsMin = new();

                public float Width;
                public float Height;
                public float X;
                public float Y;

                private bool isDirty = false;

                public void Move(string direction, float step)
                {
                    switch (direction.ToLower())
                    {
                        case "up":
                            Y += step;
                            break;
                        case "down":
                            Y -= step;
                            break;
                        case "left":
                            X -= step;
                            break;
                        case "right":
                            X += step;
                            break;
                    }

                    MarkDirty();
                }

                public void MarkDirty()
                {
                    isDirty = true;
                    cachedPositionsMin.Clear();
                    cachedPositionsMax.Clear();
                }

                [JsonIgnore] public string OffsetMin => $"{X - Width / 2f} {Y - Height / 2f}";
                [JsonIgnore] public string OffsetMax => $"{X + Width / 2f} {Y + Height / 2f}";

                public string GetOffsetMinWithIntend(int i, float intend, bool fromUpToDown = true)
                {
                    if (isDirty)
                    {
                        cachedPositionsMin.Clear();
                        isDirty = false;
                    }

                    if (cachedPositionsMin.TryGetValue((i, intend, fromUpToDown), out var cached))
                        return cached;

                    float verticalOffset = i * (Height + intend);
                    float yPos = fromUpToDown
                        ? Y - verticalOffset - Height / 2f
                        : Y + verticalOffset - Height / 2f;

                    var result = $"{X - Width / 2f} {yPos}";
                    cachedPositionsMin[(i, intend, fromUpToDown)] = result;
                    return result;
                }

                public string GetOffsetMaxWithIntend(int i, float intend, bool fromUpToDown = true)
                {
                    if (isDirty)
                    {
                        cachedPositionsMax.Clear();
                        isDirty = false;
                    }

                    if (cachedPositionsMax.TryGetValue((i, intend, fromUpToDown), out var cached))
                        return cached;

                    float verticalOffset = i * (Height + intend);
                    float yPos = fromUpToDown
                        ? Y - verticalOffset + Height / 2f
                        : Y + verticalOffset + Height / 2f;

                    var result = $"{X + Width / 2f} {yPos}";
                    cachedPositionsMax[(i, intend, fromUpToDown)] = result;
                    return result;
                }
            }

            internal class UISettings
            {
                [JsonProperty("Element conditions", Order = -1)]
                public ElementConditions Conditions;

                [JsonProperty("Fadein", Order = 0)] public float FadeIn;

                [JsonProperty("Animate feed slide-in")]
                public bool AnimateFeed;

                [JsonProperty("Animate feed slide-out (exit)")]
                public bool AnimateFeedExit;

                [JsonProperty("Animation entrance direction (Right, Left)")]
                public string AnimationInDirection;

                [JsonProperty("Animation exit direction (Right, Left)")]
                public string AnimationOutDirection;

                [JsonProperty("Animation slide distance (px)")]
                public float AnimationDistance;

                [JsonProperty("Animation duration (sec)")]
                public float AnimationDuration;

                [JsonProperty("Background color", Order = 1)]
                public string BGColor;

                [JsonProperty("Max kills panels in UI", Order = 2)]
                public int MaxPanels;

                [JsonProperty("Main panel settings", Order = 3)]
                public PanelSettings MainPanelSettings;

                [JsonProperty("Anchor settings", Order = 4)]
                public AnchorSettings AnchorSettings;

                [JsonProperty("Offset settings", Order = 5)]
                public OffsetSettings OffsetSettings;

                [JsonProperty("Killer settings", Order = 6)]
                public TextSettings KillerTextSettings;

                [JsonProperty("Target settings", Order = 7)]
                public TextSettings TargetTextSettings;

                [JsonProperty("Distance settings", Order = 8)]
                public TextSettings DistanceTextSettings;

                [JsonProperty("Headshot image (types - URL, Sprite, Shortname)", Order = 9)]
                public ColoredImage HeadshotImage;

                [JsonProperty("No-killer death text format ({0} = victim, {1} = reason)", Order = 10)]
                public string NoKillerTextFormat;

                [JsonProperty("Elements (do not touch if you know what are you doing)", Order = 99)]
                public Dictionary<string, ElementData> Elements;
            }

            internal class ElementConditions
            {
                public bool EnableTargetNickname;
                public bool EnableKillerNickname;
                public bool EnableWeapon;
                public bool EnableAmmo;
                public bool EnableDistance;
                public bool EnableHeadshot;
                public bool EnableKillerAvatar;
                public bool EnableTargetAvatar;
                public bool FromUpToDown;
            }

            internal class TextSettings : BaseElementSettings
            {
                public string TextFormat;
                public int FontSize;
                public string Font;
                public string Alignment;

                public int MaxNickLength;

                public TextAnchor GetAlign()
                {
                    switch (Alignment)
                    {
                        case "M":
                            return TextAnchor.MiddleCenter;
                        case "R":
                            return TextAnchor.MiddleRight;
                        default:
                            return TextAnchor.MiddleLeft;
                    }
                }

                public string Trim(string name)
                {
                    if (MaxNickLength <= 0 || string.IsNullOrEmpty(name) || name.Length <= MaxNickLength)
                        return name;
                    return name.Substring(0, MaxNickLength) + "…";
                }
            }

            internal abstract class BaseElementSettings
            {
                public string Color;
                public string OutlineDistance;
                public string OutlineColor;
            }

            internal class PanelSettings : BaseElementSettings
            {
            }

            public ConfigData Clone()
            {
                return JsonConvert.DeserializeObject<ConfigData>(JsonConvert.SerializeObject(this));
            }
        }

        protected override void LoadDefaultConfig()
        {
            var config = new ConfigData
            {
                ImageLoadingType = ImageLoadingType.FileManager,
                EntityToName = new()
                {
                    ["bear"] = new()
                    {
                        ["ru"] = "Медведь",
                        ["en"] = "Bear",
                        ["de"] = "Bär"
                    },
                    ["scientistnpc_outbreak"] = new()
                    {
                        ["ru"] = "Зараженный ученый",
                        ["en"] = "Outbreak Scientist",
                        ["de"] = "Ausbruch-Wissenschaftler"
                    },
                    ["gingerbread_dungeon"] = new()
                    {
                        ["ru"] = "Пряничный человек",
                        ["en"] = "Gingerbread",
                        ["de"] = "Lebkuchenmann"
                    },
                    ["scientist2"] = new()
                    {
                        ["ru"] = "Ученый",
                        ["en"] = "Scientist",
                        ["de"] = "Wissenschaftler"
                    },
                    ["crocodile"] = new()
                    {
                        ["ru"] = "Крокодил",
                        ["en"] = "Crocodile",
                        ["de"] = "Krokodil"
                    },
                    ["snake"] = new()
                    {
                        ["ru"] = "Змея",
                        ["en"] = "Snake",
                        ["de"] = "Schlange"
                    },
                    ["tiger"] = new()
                    {
                        ["ru"] = "Тигр",
                        ["en"] = "Tiger",
                        ["de"] = "Tiger"
                    },
                    ["panther"] = new()
                    {
                        ["ru"] = "Пантера",
                        ["en"] = "Panther",
                        ["de"] = "Panther"
                    },
                    ["npc_tunneldweller"] = new()
                    {
                        ["ru"] = "Обитатель тоннелей",
                        ["en"] = "Tunnel Dweller",
                        ["de"] = "Tunnelbewohner"
                    },
                    ["scientistnpc"] = new()
                    {
                        ["ru"] = "Ученый",
                        ["en"] = "Scientist",
                        ["de"] = "Wissenschaftler"
                    },
                    ["bradleyapc"] = new()
                    {
                        ["ru"] = "БТР Брэдли",
                        ["en"] = "Bradley APC",
                        ["de"] = "Bradley-Schützenpanzer"
                    },
                    ["patrolhelicopter"] = new()
                    {
                        ["ru"] = "Патрульный вертолет",
                        ["en"] = "Patrol helicopter",
                        ["de"] = "Patrouillenhubschrauber"
                    },
                    ["boar"] = new()
                    {
                        ["ru"] = "Кабан",
                        ["en"] = "Boar",
                        ["de"] = "Wildschwein"
                    },
                    ["chicken"] = new()
                    {
                        ["ru"] = "Курица",
                        ["en"] = "Chicken",
                        ["de"] = "Huhn"
                    },
                    ["horse"] = new()
                    {
                        ["ru"] = "Лошадь",
                        ["en"] = "Horse",
                        ["de"] = "Pferd"
                    },
                    ["stag"] = new()
                    {
                        ["ru"] = "Олень",
                        ["en"] = "Stag",
                        ["de"] = "Hirsch"
                    },
                    ["wolf"] = new()
                    {
                        ["ru"] = "Волк",
                        ["en"] = "Wolf",
                        ["de"] = "Wolf"
                    },
                    ["Suicide"] = new()
                    {
                        ["ru"] = "Самоубийство",
                        ["en"] = "Suicide",
                        ["de"] = "Selbstmord"
                    },
                    ["Bleeding"] = new()
                    {
                        ["ru"] = "Кровотечение",
                        ["en"] = "Bleeding",
                        ["de"] = "Blutung"
                    },
                    ["Cold"] = new()
                    {
                        ["ru"] = "Замерзание",
                        ["en"] = "Freezed",
                        ["de"] = "Erfrieren"
                    },
                    ["ColdExposure"] = new()
                    {
                        ["ru"] = "Переохлаждение",
                        ["en"] = "Freezed",
                        ["de"] = "Kälteschaden"
                    },
                    ["Fall"] = new()
                    {
                        ["ru"] = "Падение с высоты",
                        ["en"] = "Fall from high",
                        ["de"] = "Sturz aus der Höhe"
                    },
                    ["Drowned"] = new()
                    {
                        ["ru"] = "Утонул",
                        ["en"] = "Crashed",
                        ["de"] = "Ertrunken"
                    },
                    ["Radiation"] = new()
                    {
                        ["ru"] = "Радиационное отравление",
                        ["en"] = "Radiation poison",
                        ["de"] = "Strahlenvergiftung"
                    },
                    ["Hunger"] = new()
                    {
                        ["ru"] = "Умер от голода",
                        ["en"] = "Died of starvation",
                        ["de"] = "Verhungert"
                    }
                },
                AddBots = true,
                EntityToImage = new()
                {
                    ["wolf2"] = "wolf.png",
                    ["bear"] = "bear.png",
                    ["bradleyapc"] = "bradleyapc.png",
                    ["patrolhelicopter"] = "patrolhelicopter.png",
                    ["boar"] = "boar.png",
                    ["chicken"] = "chicken.png",
                    ["heavyscientist"] = "heavyscientist.png",
                    ["scientist"] = "scientist.png",
                    ["scientist2"] = "scientist.png",
                    ["scientistnvg"] = "scientistnvg.png",
                    ["dweller"] = "dweller.png",
                    ["npc_tunneldweller"] = "dweller.png",
                    ["banditguard"] = "banditguard.png",
                    ["peacekeeper"] = "peacekeeper.png",
                    ["underwaterdweller"] = "underwaterdweller.png",
                    ["shark"] = "shark.png",
                    ["polarbear"] = "polarbear.png",
                    ["scarecrow"] = "scarecrow.png",
                    ["stag"] = "stag.png",
                    ["wolf"] = "wolf.png",
                    ["crocodile"] = "crocodile.png",
                    ["snake"] = "snake.png",
                    ["tiger"] = "tiger.png",
                    ["panther"] = "panther.png",
                    ["scientistnpc_outbreak"] = "outbreakscientist.png",
                    ["gingerbread_dungeon"] = "gingerbread.png",
                    ["scientist2.heavy"] = "heavyscientist.png",
                    ["scientist2.shotgun"] = "scientist.png",
                    ["scientistnpc_arena"] = "scientist.png",
                    ["scientistnpc_bradley"] = "scientist.png",
                    ["scientistnpc_bradley_heavy"] = "heavyscientist.png",
                    ["scientistnpc_cargo"] = "scientist.png",
                    ["scientistnpc_cargo_turret_any"] = "scientist.png",
                    ["scientistnpc_cargo_turret_lr300"] = "scientist.png",
                    ["scientistnpc_ch47_gunner"] = "scientist.png",
                    ["scientistnpc_excavator"] = "scientist.png",
                    ["scientistnpc_full_any"] = "scientist.png",
                    ["scientistnpc_full_lr300"] = "scientist.png",
                    ["scientistnpc_full_mp5"] = "scientist.png",
                    ["scientistnpc_full_pistol"] = "scientist.png",
                    ["scientistnpc_full_shotgun"] = "scientist.png",
                    ["scientistnpc_heavy"] = "heavyscientist.png",
                    ["scientistnpc_junkpile_pistol"] = "scientist.png",
                    ["scientistnpc_oilrig"] = "scientist.png",
                    ["scientistnpc_patrol"] = "scientist.png",
                    ["scientistnpc_patrol_arctic"] = "scientist.png",
                    ["scientistnpc_peacekeeper"] = "peacekeeper.png",
                    ["scientistnpc_ptboat"] = "scientist.png",
                    ["scientistnpc_rhib"] = "scientist.png",
                    ["scientistnpc_roam"] = "scientist.png",
                    ["scientistnpc_roam_nvg_variant"] = "scientistnvg.png",
                    ["scientistnpc_roamtethered"] = "scientist.png",
                    ["Suicide"] = "assets/icons/skull.png",
                    ["Bleeding"] = "assets/icons/bleeding.png",
                    ["Cold"] = "assets/icons/cold.png",
                    ["ColdExposure"] = "assets/icons/cold.png",
                    ["Fall"] = "assets/icons/fall.png",
                    ["Drowned"] = "assets/icons/drowning.png",
                    ["Radiation"] = "assets/icons/radiation.png",
                    ["Hunger"] = "assets/icons/eat.png"
                },
                Lifetime = 10,
                DefaultDeathImage = new()
                {
                    Value = "assets/icons/skull.png",
                    Type = ImageType.Sprite,
                    Color = "0.8 0.82 0.88 1"
                },
                uISettings = new()
                {
                    Conditions = new()
                    {
                        EnableTargetNickname = true,
                        EnableKillerNickname = true,
                        EnableWeapon = true,
                        EnableAmmo = true,
                        EnableDistance = true,
                        EnableHeadshot = true,
                        EnableKillerAvatar = true,
                        EnableTargetAvatar = true,
                        FromUpToDown = true
                    },
                    Elements = new()
                    {
                        ["kill_panel"] = new ConfigData.ElementData
                        {
                            Width = 350f,
                            Height = 25f,
                            X = -180f,
                            Y = -20.5f,
                        },
                        ["killer_div"] = new ConfigData.ElementData
                        {
                            Width = 116f,
                            Height = 25f,
                            X = -59f,
                            Y = -12.5f
                        },
                        ["weapon_icon"] = new ConfigData.ElementData
                        {
                            Width = 23f,
                            Height = 23f,
                            X = -23f,
                            Y = 0f,
                        },
                        ["ammo_icon"] = new ConfigData.ElementData
                        {
                            Width = 23f,
                            Height = 23f,
                            X = 0.5f,
                            Y = 0f
                        },
                        ["distance_text"] = new ConfigData.ElementData
                        {
                            Width = 45f,
                            Height = 25f,
                            X = 45f,
                            Y = 0f
                        },
                        ["headshot_icon"] = new ConfigData.ElementData
                        {
                            Width = 17f,
                            Height = 17f,
                            X = 58f,
                            Y = 0f,
                        },
                        ["killer_text"] = new()
                        {
                            Width = 80,
                            Height = 20,
                            X = 12,
                            Y = 0
                        },
                        ["target_text"] = new()
                        {
                            Width = 80,
                            Height = 20,
                            X = -10,
                            Y = 0
                        },
                        ["nokiller_text"] = new()
                        {
                            Width = 323.77f,
                            Height = 24.816f,
                            X = -16.37f,
                            Y = 0f
                        },
                        ["nokiller_icon"] = new()
                        {
                            Width = 24.816f,
                            Height = 24.816f,
                            X = 163.7f,
                            Y = 0f
                        },
                        ["killer_avatar"] = new ConfigData.ElementData
                        {
                            Width = 21f,
                            Height = 21f,
                            X = -44f,
                            Y = 0f
                        },
                        ["target_avatar"] = new ConfigData.ElementData
                        {
                            Width = 21f,
                            Height = 21f,
                            X = 44f,
                            Y = 0f
                        },
                    },
                    FadeIn = 1f,
                    AnimateFeed = true,
                    AnimateFeedExit = true,
                    AnimationInDirection = "Right",
                    AnimationOutDirection = "Right",
                    AnimationDistance = 160f,
                    AnimationDuration = 0.2f,
                    BGColor = "0.09 0.1 0.13 0.88",
                    NoKillerTextFormat = "{0} | {1}",
                    MaxPanels = 3,
                    OffsetSettings = new()
                    {
                        OffsetMinX = 5f,
                        OffsetMaxX = -4.899963f,
                        OffsetMinY = 10f,
                        OffsetMaxY = -5.100018f,
                        Indent = 3f
                    },
                    KillerTextSettings = new()
                    {
                        TextFormat = "{0}",
                        FontSize = 12,
                        Font = FontBold,
                        Color = "0.96 0.97 0.99 1",
                        OutlineDistance = "-1 1",
                        OutlineColor = "0 0 0 1",
                        Alignment = "L"
                    },
                    TargetTextSettings = new()
                    {
                        TextFormat = "{0}",
                        FontSize = 12,
                        Font = FontBold,
                        Color = "0.96 0.97 0.99 1",
                        OutlineDistance = "-1 1",
                        OutlineColor = "0 0 0 1",
                        Alignment = "R"
                    },
                    DistanceTextSettings = new()
                    {
                        TextFormat = "{0}m",
                        FontSize = 12,
                        Font = FontRegular,
                        Color = "0.55 0.74 0.95 1",
                        OutlineDistance = "-1 1",
                        OutlineColor = "0 0 0 1",
                        Alignment = "L"
                    },
                    HeadshotImage = new()
                    {
                        Value = "assets/icons/skull.png",
                        Type = ImageType.Sprite,
                        Color = "1 0.82 0.38 1"
                    },
                    AnchorSettings = new()
                    {
                        AnchorMin = "1 1",
                        AnchorMax = "1 1"
                    }
                },
                Version = new(1, 0, 11),
                AddAnimals = true
            };
            SaveConfig(config);
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                cfg = Config.ReadObject<ConfigData>();
            }
            catch
            {

                PrintWarning("[Config] Failed to parse config, attempting to migrate removed image mode...");
                var raw = Config.ReadObject<Dictionary<string, object>>();
                foreach (var key in new List<object>(raw.Keys))
                {
                    if (key is string s && s.Contains("method of download images") &&
                        raw[s]?.ToString() == "Internal")
                        raw[s] = ImageLoadingType.FileManager.ToString();
                }

                Config.WriteObject(raw, true);
                cfg = Config.ReadObject<ConfigData>();
            }

            var needUpdateCfg = false;
            if (cfg.Version == null)
            {
                cfg.Version = new(1, 0, 0);
                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 1))
            {
                cfg.Version = new(1, 0, 1);
                cfg.AddAnimals = true;
                cfg.AddPatrolHeli = true;
                cfg.AddBradley = true;

                cfg.uISettings.DistanceTextSettings.OutlineColor = "0 0 0 1";
                cfg.uISettings.DistanceTextSettings.OutlineDistance = "-1 1";
                cfg.uISettings.TargetTextSettings.OutlineColor = "0 0 0 1";
                cfg.uISettings.TargetTextSettings.OutlineDistance = "-1 1";
                cfg.uISettings.KillerTextSettings.OutlineColor = "0 0 0 1";
                cfg.uISettings.KillerTextSettings.OutlineDistance = "-1 1";

                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 2))
            {
                cfg.Version = new(1, 0, 2);

                cfg.uISettings.AnchorSettings = new();
                cfg.uISettings.AnchorSettings.AnchorMin = "1 1";
                cfg.uISettings.AnchorSettings.AnchorMax = "1 1";

                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 3))
            {
                cfg.Version = new(1, 0, 3);

                cfg.ImageLoadingType = ImageLoadingType.FileManager;
                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 4))
            {
                cfg.Version = new VersionNumber(1, 0, 4);

                cfg.EntityToName = new()
                {
                    ["bear"] = new()
                    {
                        ["ru"] = "Медведь",
                        ["en"] = "Bear",
                        ["de"] = "Bär"
                    },
                    ["scientistnpc"] = new()
                    {
                        ["ru"] = "Ученый",
                        ["en"] = "Scientist",
                        ["de"] = "Wissenschaftler"
                    },
                    ["bradleyapc"] = new()
                    {
                        ["ru"] = "БТР Брэдли",
                        ["en"] = "Bradley APC",
                        ["de"] = "Bradley-Schützenpanzer"
                    },
                    ["patrolhelicopter"] = new()
                    {
                        ["ru"] = "Патрульный вертолет",
                        ["en"] = "Patrol helicopter",
                        ["de"] = "Patrouillenhubschrauber"
                    },
                    ["boar"] = new()
                    {
                        ["ru"] = "Кабан",
                        ["en"] = "Boar",
                        ["de"] = "Wildschwein"
                    },
                    ["chicken"] = new()
                    {
                        ["ru"] = "Курица",
                        ["en"] = "Chicken",
                        ["de"] = "Huhn"
                    },
                    ["horse"] = new()
                    {
                        ["ru"] = "Лошадь",
                        ["en"] = "Horse",
                        ["de"] = "Pferd"
                    },
                    ["stag"] = new()
                    {
                        ["ru"] = "Олень",
                        ["en"] = "Stag",
                        ["de"] = "Hirsch"
                    },
                    ["wolf"] = new()
                    {
                        ["ru"] = "Волк",
                        ["en"] = "Wolf",
                        ["de"] = "Wolf"
                    },
                    ["Suicide"] = new()
                    {
                        ["ru"] = "Самоубийство",
                        ["en"] = "Suicide",
                        ["de"] = "Selbstmord"
                    },
                    ["Bleeding"] = new()
                    {
                        ["ru"] = "Кровотечение",
                        ["en"] = "Bleeding",
                        ["de"] = "Blutung"
                    },
                    ["Cold"] = new()
                    {
                        ["ru"] = "Замерзание",
                        ["en"] = "Freezed",
                        ["de"] = "Erfrieren"
                    },
                    ["ColdExposure"] = new()
                    {
                        ["ru"] = "Переохлаждение",
                        ["en"] = "Freezed",
                        ["de"] = "Kälteschaden"
                    },
                    ["Fall"] = new()
                    {
                        ["ru"] = "Падение с высоты",
                        ["en"] = "Fall from high",
                        ["de"] = "Sturz aus der Höhe"
                    },
                    ["Drowned"] = new()
                    {
                        ["ru"] = "Утонул",
                        ["en"] = "Crashed",
                        ["de"] = "Ertrunken"
                    },
                    ["Radiation"] = new()
                    {
                        ["ru"] = "Радиационное отравление",
                        ["en"] = "Radiation poison",
                        ["de"] = "Strahlenvergiftung"
                    },
                    ["Hunger"] = new()
                    {
                        ["ru"] = "Умер от голода",
                        ["en"] = "Died of starvation",
                        ["de"] = "Verhungert"
                    }
                };

                cfg.ImageLoadingType = ImageLoadingType.FileManager;
                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 5))
            {
                cfg.Version = new VersionNumber(1, 0, 5);

                cfg.uISettings.Conditions = new()
                {
                    EnableTargetNickname = true,
                    EnableKillerNickname = true,
                    EnableWeapon = true,
                    EnableAmmo = true,
                    EnableDistance = true,
                    EnableHeadshot = true,
                    FromUpToDown = true
                };
                cfg.uISettings.Elements = new()
                {
                    ["kill_panel"] = new ConfigData.ElementData
                    {
                        Width = 350f,
                        Height = 25f,
                        X = -180f,
                        Y = -20.5f,
                    },
                    ["killer_div"] = new ConfigData.ElementData
                    {
                        Width = 116f,
                        Height = 25f,
                        X = -59f,
                        Y = -12.5f
                    },
                    ["weapon_icon"] = new ConfigData.ElementData
                    {
                        Width = 23f,
                        Height = 23f,
                        X = -22f,
                        Y = 0f,
                    },
                    ["ammo_icon"] = new ConfigData.ElementData
                    {
                        Width = 23f,
                        Height = 23f,
                        X = 0.5f,
                        Y = 0f
                    },
                    ["distance_text"] = new ConfigData.ElementData
                    {
                        Width = 45f,
                        Height = 25f,
                        X = 45f,
                        Y = 0f
                    },
                    ["headshot_icon"] = new ConfigData.ElementData
                    {
                        Width = 17f,
                        Height = 17f,
                        X = 54f,
                        Y = 0f,
                    },
                    ["killer_text"] = new ConfigData.ElementData()
                    {
                        Width = 30,
                        Height = 20,
                        X = -20,
                        Y = 0
                    },
                    ["target_text"] = new ConfigData.ElementData()
                    {
                        Width = 30,
                        Height = 20,
                        X = 20,
                        Y = 0
                    },
                    ["nokiller_text"] = new ConfigData.ElementData()
                    {
                        Width = 323.77f,
                        Height = 24.816f,
                        X = -16.37f,
                        Y = 0f
                    },
                    ["nokiller_icon"] = new ConfigData.ElementData()
                    {
                        Width = 24.816f,
                        Height = 24.816f,
                        X = 163.7f,
                        Y = 0f
                    }
                };
                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 6))
            {
                cfg.Version = new VersionNumber(1, 0, 6);

                var killer = cfg.uISettings.Elements["killer_text"];
                var target = cfg.uISettings.Elements["target_text"];
                if (killer.Width == 30 && killer.X == -20)
                    cfg.uISettings.Elements["killer_text"] = new()
                    {
                        Width = 80,
                        Height = 20,
                        X = -10,
                        Y = 0
                    };

                if (target.Width == 30 && target.X == 20)
                    cfg.uISettings.Elements["target_text"] = new()
                    {
                        Width = 80,
                        Height = 20,
                        X = 10,
                        Y = 0
                    };

                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 7))
            {
                cfg.Version = new(1, 0, 7);
                cfg.uISettings.DistanceTextSettings.Alignment = "L";
                cfg.uISettings.KillerTextSettings.Alignment = "L";
                cfg.uISettings.TargetTextSettings.Alignment = "R";

                cfg.DefaultDeathImage.Color = "0.5499007 0.4764151 1 1";
                cfg.uISettings.HeadshotImage.Color = "0.5499007 0.4764151 1 1";

                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 8))
            {
                cfg.Version = new(1, 0, 8);

                if (string.IsNullOrEmpty(cfg.uISettings.NoKillerTextFormat))
                    cfg.uISettings.NoKillerTextFormat = "{0} | {1}";

                if (!cfg.uISettings.Elements.ContainsKey("nokiller_text"))
                    cfg.uISettings.Elements["nokiller_text"] = new()
                    {
                        Width = 323.77f, Height = 24.816f, X = -16.37f, Y = 0f
                    };

                if (!cfg.uISettings.Elements.ContainsKey("nokiller_icon"))
                    cfg.uISettings.Elements["nokiller_icon"] = new()
                    {
                        Width = 24.816f, Height = 24.816f, X = 163.7f, Y = 0f
                    };

                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 9))
            {
                cfg.Version = new VersionNumber(1, 0, 9);

                cfg.uISettings.Conditions.EnableKillerAvatar = true;
                cfg.uISettings.Conditions.EnableTargetAvatar = true;

                if (!cfg.uISettings.Elements.ContainsKey("killer_avatar"))
                    cfg.uISettings.Elements["killer_avatar"] = new ConfigData.ElementData
                    {
                        Width = 21f,
                        Height = 21f,
                        X = -46f,
                        Y = 0f
                    };

                if (!cfg.uISettings.Elements.ContainsKey("target_avatar"))
                    cfg.uISettings.Elements["target_avatar"] = new ConfigData.ElementData
                    {
                        Width = 21f,
                        Height = 21f,
                        X = 46f,
                        Y = 0f
                    };

                cfg.uISettings.AnimateFeed = false;
                cfg.uISettings.AnimateFeedExit = true;
                cfg.uISettings.AnimationInDirection = "Right";
                cfg.uISettings.AnimationOutDirection = "Right";
                cfg.uISettings.AnimationDistance = 160f;
                cfg.uISettings.AnimationDuration = 0.2f;

                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 10))
            {
                cfg.Version = new VersionNumber(1, 0, 10);

                var newImages = new Dictionary<string, string>
                {
                    ["crocodile"] = "crocodile.png",
                    ["snake"] = "snake.png",
                    ["tiger"] = "tiger.png",
                    ["panther"] = "panther.png",
                    ["scientistnpc_outbreak"] = "outbreakscientist.png",
                    ["gingerbread_dungeon"] = "gingerbread.png",
                    ["scientist2"] = "scientist.png",
                    ["npc_tunneldweller"] = "dweller.png"
                };

                cfg.EntityToImage ??= new();
                foreach (var pair in newImages)
                    if (!cfg.EntityToImage.ContainsKey(pair.Key))
                        cfg.EntityToImage[pair.Key] = pair.Value;

                var newNames = new Dictionary<string, Dictionary<string, string>>
                {
                    ["scientistnpc_outbreak"] = new()
                    {
                        ["ru"] = "Зараженный ученый",
                        ["en"] = "Outbreak Scientist",
                        ["de"] = "Ausbruch-Wissenschaftler"
                    },
                    ["gingerbread_dungeon"] = new()
                    {
                        ["ru"] = "Пряничный человек",
                        ["en"] = "Gingerbread",
                        ["de"] = "Lebkuchenmann"
                    },
                    ["scientist2"] = new()
                    {
                        ["ru"] = "Ученый",
                        ["en"] = "Scientist",
                        ["de"] = "Wissenschaftler"
                    },
                    ["crocodile"] = new()
                    {
                        ["ru"] = "Крокодил",
                        ["en"] = "Crocodile",
                        ["de"] = "Krokodil"
                    },
                    ["snake"] = new()
                    {
                        ["ru"] = "Змея",
                        ["en"] = "Snake",
                        ["de"] = "Schlange"
                    },
                    ["tiger"] = new()
                    {
                        ["ru"] = "Тигр",
                        ["en"] = "Tiger",
                        ["de"] = "Tiger"
                    },
                    ["panther"] = new()
                    {
                        ["ru"] = "Пантера",
                        ["en"] = "Panther",
                        ["de"] = "Panther"
                    },
                    ["npc_tunneldweller"] = new()
                    {
                        ["ru"] = "Обитатель тоннелей",
                        ["en"] = "Tunnel Dweller",
                        ["de"] = "Tunnelbewohner"
                    }
                };

                cfg.EntityToName ??= new();
                foreach (var pair in newNames)
                    if (!cfg.EntityToName.ContainsKey(pair.Key))
                        cfg.EntityToName[pair.Key] = pair.Value;

                needUpdateCfg = true;
            }

            if (cfg.Version < new VersionNumber(1, 0, 11))
            {
                cfg.Version = new VersionNumber(1, 0, 11);

                var scientistImages = new Dictionary<string, string>
                {
                    ["scientist2.heavy"] = "heavyscientist.png",
                    ["scientist2.shotgun"] = "scientist.png",
                    ["scientistnpc_arena"] = "scientist.png",
                    ["scientistnpc_bradley"] = "scientist.png",
                    ["scientistnpc_bradley_heavy"] = "heavyscientist.png",
                    ["scientistnpc_cargo"] = "scientist.png",
                    ["scientistnpc_cargo_turret_any"] = "scientist.png",
                    ["scientistnpc_cargo_turret_lr300"] = "scientist.png",
                    ["scientistnpc_ch47_gunner"] = "scientist.png",
                    ["scientistnpc_excavator"] = "scientist.png",
                    ["scientistnpc_full_any"] = "scientist.png",
                    ["scientistnpc_full_lr300"] = "scientist.png",
                    ["scientistnpc_full_mp5"] = "scientist.png",
                    ["scientistnpc_full_pistol"] = "scientist.png",
                    ["scientistnpc_full_shotgun"] = "scientist.png",
                    ["scientistnpc_heavy"] = "heavyscientist.png",
                    ["scientistnpc_junkpile_pistol"] = "scientist.png",
                    ["scientistnpc_oilrig"] = "scientist.png",
                    ["scientistnpc_patrol"] = "scientist.png",
                    ["scientistnpc_patrol_arctic"] = "scientist.png",
                    ["scientistnpc_peacekeeper"] = "peacekeeper.png",
                    ["scientistnpc_ptboat"] = "scientist.png",
                    ["scientistnpc_rhib"] = "scientist.png",
                    ["scientistnpc_roam"] = "scientist.png",
                    ["scientistnpc_roam_nvg_variant"] = "scientistnvg.png",
                    ["scientistnpc_roamtethered"] = "scientist.png"
                };

                cfg.EntityToImage ??= new();
                foreach (var pair in scientistImages)
                    if (!cfg.EntityToImage.ContainsKey(pair.Key))
                        cfg.EntityToImage[pair.Key] = pair.Value;

                needUpdateCfg = true;
            }

            if (needUpdateCfg)
                PrintWarning("Config was updated!");

            SaveConfig(cfg);
        }

        private void SaveConfig(object config)
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Title}/disabledUI", _disabledFeed);
            Interface.Oxide.DataFileSystem.WriteObject($"{Title}/storedImages", _imageList);
            Interface.Oxide.DataFileSystem.WriteObject($"{Title}/adminThemes", _adminThemes);
        }

        private void LoadData()
        {
            _disabledFeed = Interface.Oxide?.DataFileSystem?.ReadObject<List<ulong>>($"{Title}/disabledUI")
                            ?? new();
            _imageList =
                Interface.Oxide?.DataFileSystem?.ReadObject<Dictionary<string, string>>($"{Title}/storedImages")
                ?? new();
            _adminThemes = Interface.Oxide?.DataFileSystem?.ReadObject<Dictionary<ulong, UITheme>>($"{Title}/adminThemes")
                           ?? new();
        }

        private void RevalidateStoredImages()
        {
            if (_imageList == null || _imageList.Count == 0)
                return;

            var staleKeys = new List<string>();
            foreach (var kvp in _imageList)
            {
                if (string.IsNullOrEmpty(kvp.Value))
                {
                    staleKeys.Add(kvp.Key);
                    continue;
                }

                if (uint.TryParse(kvp.Value, out var uid))
                {
                    try
                    {
                        if (FileStorage.server.Get(uid, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID) ==
                            null)
                            staleKeys.Add(kvp.Key);
                    }
                    catch
                    {
                        staleKeys.Add(kvp.Key);
                    }
                }
            }

            if (staleKeys.Count > 0)
            {
                foreach (var key in staleKeys)
                    _imageList.Remove(key);

                PrintWarning(
                    $"[ImageSystem] Removed {staleKeys.Count} stale image references. They will be re-downloaded.");
            }
        }

        #endregion
    }
}