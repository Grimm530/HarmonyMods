/*
< ----- End-User License Agreement ----->

This software and all associated files (“Software”) are the intellectual property of the Developer.  
By installing, loading, or using this Software, you agree to the following terms:

1. You may not merge, publish, redistribute, sublicense, or sell this Software or any modified versions of it without the Developer’s explicit written consent.

2. You may copy or modify the Software **only for personal, private use on servers you own or operate**.  
   Distribution of modified or unmodified versions to any third party is strictly prohibited.

3. THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDER AND CONTRIBUTORS "AS IS" AND WITHOUT WARRANTY OF ANY KIND, 
   EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NON-INFRINGEMENT.
   
4. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES 
   (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) 
   HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
   ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Developer: Grimm530 (r3ap3rsg@gmail.com)

Copyright © Grimm530. All rights reserved.
*/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

namespace RustVehiclesGUIHarmony
{
    /// <summary>
    /// RustVehiclesGUI 1.0.5 ported for Harmony (no Oxide). Logic matches the Oxide plugin; hosting differs.
    /// Vehicle purchases and spawns are delegated to the RustVehicles Harmony mod.
    /// </summary>
    [Info("Rust Vehicles GUI", "Grimm530", "1.0.5")]
    [Description("GUI interface for Rust Vehicles plugin with ServerPanel integration")]
    public class RustVehiclesGUI : RustVehiclesGUIPluginBase
    {
        #region Fields

        private Plugin RustVehicles => PluginBridges.RustVehicles;
        private Plugin VehicleLicence => PluginBridges.VehicleLicence;
        private Plugin Economics => PluginBridges.Economics;
        private Plugin ServerRewards => PluginBridges.ServerRewards;
        private Plugin ServerPanel => PluginBridges.ServerPanel;

        private static RustVehiclesGUI Instance;
        private ConfigData _config;
        private HashSet<string> _availableCustomVehicles = new HashSet<string>();
        private readonly Dictionary<string, string> _karuzaVehicleCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private const string UI_MAIN = "RustVehiclesGUI.Main";
        private const string UI_SHOP = "RustVehiclesGUI.Shop";
        private const string UI_MANAGE = "RustVehiclesGUI.Manage";
        
        private readonly Dictionary<ulong, int> _playerShopPage = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> _playerManagePage = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, Queue<ImageTask>> _imageQueues = new Dictionary<ulong, Queue<ImageTask>>();
        private readonly HashSet<ulong> _imageQueueActive = new HashSet<ulong>();
        private readonly Dictionary<ulong, string> _playerSelectedCategory = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _playerSelectedManageCategory = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _playerServerPanelView = new Dictionary<ulong, string>();
        private readonly Dictionary<string, List<VehicleDisplayInfo>> _cachedVehicleLists = new Dictionary<string, List<VehicleDisplayInfo>>();
        private readonly Dictionary<string, ulong> _cachedVehicleListPlayer = new Dictionary<string, ulong>();
        private Plugin _cachedCorePlugin = null;
        private bool _corePluginCacheValid = false;
        
        private void ClearVehicleListCache(ulong playerId)
        {
            var keysToRemove = new List<string>();
            foreach (var kvp in _cachedVehicleListPlayer)
            {
                if (kvp.Value == playerId)
                    keysToRemove.Add(kvp.Key);
            }
            foreach (var key in keysToRemove)
            {
                _cachedVehicleLists.Remove(key);
                _cachedVehicleListPlayer.Remove(key);
            }
            DebugUI($"[CACHE] Cleared vehicle list cache for player {playerId} ({keysToRemove.Count} entries)");
        }
        private readonly Dictionary<string, uint> _imageKeyToPngId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, string> _playerBackgroundColor = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, float> _playerTransparency = new Dictionary<ulong, float>();
        private const float DEFAULT_TRANSPARENCY = 30f;
        private readonly Dictionary<ulong, HashSet<string>> _playerOwnedVehiclesCache = new Dictionary<ulong, HashSet<string>>();
        
        private Dictionary<ulong, PlayerUISettings> _playerSettings = new Dictionary<ulong, PlayerUISettings>();

        private Dictionary<string, object> _cachedVehicleConfig = null;
        private DateTime _configCacheTimestamp = DateTime.MinValue;
        private readonly object _configCacheLock = new object();
        private static readonly TimeSpan CONFIG_CACHE_DURATION = TimeSpan.FromMinutes(5);

        private readonly Dictionary<ulong, Timer> _pendingSaveTimers = new Dictionary<ulong, Timer>();
        private static readonly float SAVE_DEBOUNCE_DELAY = 2.0f;

        private Timer _pluginReloadDelayTimer = null;
        private static readonly float PLUGIN_RELOAD_DELAY = 10.0f;

		private class ImageTask
        {
            public string Parent;
            public string AnchorMin;
            public string AnchorMax;
			public string ImageKey;
			public string ImageTitle;
        }

        private const int VEHICLES_PER_PAGE = 16;

        #endregion

        #region Hooks

        internal void Init()
        {
            Instance = this;
            LoadDefaultMessages();
            _config = Config.ReadObject<ConfigData>();
            
            foreach (var command in _config.ChatCommands)
            {
                cmd.AddChatCommand(command, this, nameof(CmdOpenGUI));
            }
        }

        private string Lang(string key, BasePlayer player, params object[] args)
        {
            return Lang(key, player?.UserIDString, args);
        }

        private string Lang(string key, string userId, params object[] args)
        {
            try
            {
                var message = lang.GetMessage(key, this, userId);
                if (args != null && args.Length > 0)
                    return string.Format(message, args);
                return message;
            }
            catch (Exception)
            {
                return key;
            }
        }

        private string LangCategory(BasePlayer player, string category)
        {
            var translated = Lang("Category_" + category, player);
            if (string.IsNullOrEmpty(translated) || translated.StartsWith("Category_", StringComparison.Ordinal))
                return (category ?? string.Empty).ToUpperInvariant();
            return translated;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CorePluginNotLoaded"] = "Vehicle system plugin is not loaded!",
                ["NoPermission"] = "You don't have permission to use vehicles!",
                ["VipOnly"] = "This vehicle is VIP only.",
                ["PickupRequiresRustVehicles"] = "Please purchase RustVehicles to use this feature.",
                ["ImageRegistry"] = "Image registry: {0} keys (see server log)",

                ["TitleMain"] = "Rust Vehicles System",
                ["TitleShop"] = "Vehicle Shop",
                ["TitleManage"] = "Manage Your Vehicles",
                ["VehiclesCount"] = "Vehicles: {0}",
                ["Balance"] = "Balance:",
                ["BuyVehicles"] = "Buy Vehicles",
                ["ManageVehicles"] = "Manage Vehicles",
                ["Back"] = "← Back",
                ["BackMain"] = "← Main",
                ["PageOf"] = "Page {0} of {1}",
                ["NoVehiclesPurchase"] = "No vehicles available for purchase",
                ["NoVehiclesCategory"] = "You don't own any vehicles in this category",
                ["NoVehiclesYet"] = "You don't own any vehicles yet",
                ["AddImage"] = "Add image: {0}",
                ["FailedLoadShop"] = "Failed to load vehicle shop. Please try again.",
                ["ErrorLoadingMain"] = "Error loading main menu: {0}",
                ["ErrorLoadingVehicles"] = "Error loading vehicles: {0}",
                ["ErrorLoadingShop"] = "Error loading vehicle shop: {0}",

                ["Buy"] = "BUY",
                ["Spawn"] = "SPAWN",
                ["Recall"] = "RECALL",
                ["Pickup"] = "PICKUP",
                ["Spawned"] = "SPAWNED",

                ["Free"] = "Free",
                ["UnknownPrice"] = "Unknown Price",
                ["PriceUnknown"] = "Price Unknown",
                ["AlreadyOwned"] = "Already Owned",
                ["CurrentlySpawned"] = "Currently Spawned",
                ["AvailableToSpawn"] = "Available to Spawn",
                ["Available"] = "Available",
                ["Cooldown"] = "Cooldown: {0}",
                ["Unknown"] = "Unknown",

                ["Category_all"] = "ALL",
                ["Category_air"] = "AIR",
                ["Category_land"] = "LAND",
                ["Category_water"] = "WATER",
                ["Category_train"] = "TRAIN",
                ["Category_siege"] = "SIEGE"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CorePluginNotLoaded"] = "Плагин транспортной системы не загружен!",
                ["NoPermission"] = "У вас нет прав для использования транспорта!",
                ["VipOnly"] = "Этот транспорт только для VIP.",
                ["PickupRequiresRustVehicles"] = "Для этой функции требуется RustVehicles.",
                ["ImageRegistry"] = "Реестр изображений: {0} ключей (см. лог сервера)",

                ["TitleMain"] = "Система транспорта Rust",
                ["TitleShop"] = "Магазин транспорта",
                ["TitleManage"] = "Управление транспортом",
                ["VehiclesCount"] = "Транспорт: {0}",
                ["Balance"] = "Баланс:",
                ["BuyVehicles"] = "Купить транспорт",
                ["ManageVehicles"] = "Управление транспортом",
                ["Back"] = "← Назад",
                ["BackMain"] = "← Меню",
                ["PageOf"] = "Стр. {0} из {1}",
                ["NoVehiclesPurchase"] = "Нет транспорта для покупки",
                ["NoVehiclesCategory"] = "В этой категории нет вашего транспорта",
                ["NoVehiclesYet"] = "У вас ещё нет транспорта",
                ["AddImage"] = "Добавьте изображение: {0}",
                ["FailedLoadShop"] = "Не удалось загрузить магазин. Попробуйте снова.",
                ["ErrorLoadingMain"] = "Ошибка загрузки главного меню: {0}",
                ["ErrorLoadingVehicles"] = "Ошибка загрузки транспорта: {0}",
                ["ErrorLoadingShop"] = "Ошибка загрузки магазина: {0}",

                ["Buy"] = "КУПИТЬ",
                ["Spawn"] = "СОЗДАТЬ",
                ["Recall"] = "ВЫЗВАТЬ",
                ["Pickup"] = "ПОДОБРАТЬ",
                ["Spawned"] = "СОЗДАН",

                ["Free"] = "Бесплатно",
                ["UnknownPrice"] = "Цена неизвестна",
                ["PriceUnknown"] = "Цена неизвестна",
                ["AlreadyOwned"] = "Уже куплено",
                ["CurrentlySpawned"] = "Уже создан",
                ["AvailableToSpawn"] = "Можно создать",
                ["Available"] = "Доступен",
                ["Cooldown"] = "Ожидание: {0}",
                ["Unknown"] = "Неизвестно",

                ["Category_all"] = "ВСЕ",
                ["Category_air"] = "ВОЗДУХ",
                ["Category_land"] = "СУША",
                ["Category_water"] = "ВОДА",
                ["Category_train"] = "ПОЕЗДА",
                ["Category_siege"] = "ОСАДА"
            }, this, "ru");
        }

        internal void OnServerInitialized()
        {
            RegisterGradientImages();
            LoadPlayerSettings();
            
            ServerMgr.Instance.StartCoroutine(SoftStartInitialize());
        }

        private IEnumerator SoftStartInitialize()
        {
            yield return CoroutineEx.waitForSeconds(0.5f);
            
            while (Performance.report.frameRate < 15 && ConVar.FPS.limit > 15)
            {
                yield return CoroutineEx.waitForSeconds(1f);
            }
            
            CheckAvailableCustomVehicles();
            yield return CoroutineEx.waitForSeconds(0.1f);
            
            LoadKaruzaVehicleCategories();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin?.Name == "VehicleLicence")
            {
                DebugUI("[CACHE] VehicleLicence plugin loaded - scheduling cache invalidation after delay to prevent packet flooding");
                
                if (_pluginReloadDelayTimer != null && !_pluginReloadDelayTimer.Destroyed)
                {
                    _pluginReloadDelayTimer.Destroy();
                }
                
                _pluginReloadDelayTimer = timer.Once(PLUGIN_RELOAD_DELAY, () =>
                {
                    DebugUI("[CACHE] Delay completed - invalidating all caches now");
                    InvalidateAllCaches();
                    _pluginReloadDelayTimer = null;
                });
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin?.Name == "VehicleLicence")
            {
                DebugUI("[CACHE] VehicleLicence plugin unloaded - scheduling cache invalidation after delay to prevent packet flooding");
                
                if (_pluginReloadDelayTimer != null && !_pluginReloadDelayTimer.Destroyed)
                {
                    _pluginReloadDelayTimer.Destroy();
                }
                
                _pluginReloadDelayTimer = timer.Once(PLUGIN_RELOAD_DELAY, () =>
                {
                    DebugUI("[CACHE] Delay completed - invalidating all caches now");
                    InvalidateAllCaches();
                    _pluginReloadDelayTimer = null;
                });
            }
        }

        private void InvalidateAllCaches()
        {
            lock (_configCacheLock)
            {
                _cachedVehicleConfig = null;
                _configCacheTimestamp = DateTime.MinValue;
            }

            _cachedCorePlugin = null;
            _corePluginCacheValid = false;

            _cachedVehicleLists.Clear();
            _cachedVehicleListPlayer.Clear();

            DebugUI("[CACHE] All caches invalidated");
        }

        internal void OnServerShutdown()
        {
            var userIdsToSave = new List<ulong>(_pendingSaveTimers.Keys);
            foreach (var userId in userIdsToSave)
            {
                if (_pendingSaveTimers.TryGetValue(userId, out var timer))
                {
                    timer.Destroy();
                    SavePlayerSettingsInternal(userId);
                }
            }
            _pendingSaveTimers.Clear();
            
            if (_pluginReloadDelayTimer != null && !_pluginReloadDelayTimer.Destroyed)
            {
                _pluginReloadDelayTimer.Destroy();
                _pluginReloadDelayTimer = null;
            }
        }

        private void CheckAvailableCustomVehicles()
        {
            _availableCustomVehicles.Clear();
            
            CheckVehiclesConfigForCustomVehicles();
            
            var karuzaVehicles = LoadCustomVehiclesFromKaruzaManager();
            foreach (var vehicle in karuzaVehicles)
            {
                _availableCustomVehicles.Add(vehicle);
            }
            
            DebugKaruzaVehicles($"Total custom vehicles available: {_availableCustomVehicles.Count}");
            DebugKaruzaVehicles($"From RustVehicles config + KaruzaManager: {_availableCustomVehicles.Count}");
            if (_availableCustomVehicles.Count > 0)
            {
                var first20 = new List<string>();
                int count = 0;
                foreach (var vehicle in _availableCustomVehicles)
                {
                    if (count >= 20) break;
                    first20.Add(vehicle);
                    count++;
                }
                DebugKaruzaVehicles($"Available custom vehicles: {string.Join(", ", first20)}...");
            }
        }



        private void CheckVehiclesConfigForCustomVehicles()
        {
            try
            {
				var configPath = GetCoreConfigPath();
                DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] Checking vehicle config path: {configPath}");
                
				if (!System.IO.File.Exists(configPath))
                {
                    DebugKaruzaVehicles($"Vehicle config not found at: {configPath}");
                    return;
                }
                
                DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] Config file exists, reading content...");
                DebugKaruzaVehicles($"[CONFIG] Reading vehicles from: {configPath}");
                var jsonContent = System.IO.File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                
                DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] Config deserialized, keys: {string.Join(", ", config.Keys)}");
                
                if (config.ContainsKey("Normal Vehicle Settings"))
                {
                    DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] Found 'Normal Vehicle Settings' section");
                    if (config["Normal Vehicle Settings"] is Newtonsoft.Json.Linq.JObject normalVehicleSettings)
                    {
                        int propCount = 0;
                        foreach (var prop in normalVehicleSettings.Properties()) propCount++;
                        DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] Normal Vehicle Settings has {propCount} vehicles");
                        
                        foreach (var vehicleProperty in normalVehicleSettings.Properties())
                        {
                            var vehicleName = vehicleProperty.Name;
                            var vehicleData = vehicleProperty.Value as Newtonsoft.Json.Linq.JObject;
                            
                            if (vehicleData != null)
                            {
                                var prefabPath = vehicleData["Prefab Path"]?.ToString();
                                if (!string.IsNullOrEmpty(prefabPath) && prefabPath.Contains("assets/custom/"))
                                {
                                    var vehicleType = vehicleName.ToLower();
                                    _availableCustomVehicles.Add(vehicleType);
                                    DebugKaruzaVehicles($"Found custom vehicle in Normal Vehicle Settings: {vehicleType} (Prefab: {prefabPath})");
                                }
                            }
                        }
                    }
                    else
                    {
                        DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] Normal Vehicle Settings is not a JObject: {config["Normal Vehicle Settings"]?.GetType()}");
                    }
                }
                else
                {
                    DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] 'Normal Vehicle Settings' section not found");
                }
                
                if (config.ContainsKey("Custom Vehicle Settings"))
                {
                    DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] Found 'Custom Vehicle Settings' section");
                    if (config["Custom Vehicle Settings"] is Newtonsoft.Json.Linq.JObject customVehicleSettings)
                    {
                        int propCount = 0;
                        foreach (var prop in customVehicleSettings.Properties()) propCount++;
                        DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] Custom Vehicle Settings has {propCount} vehicles");
                        
                        foreach (var vehicleProperty in customVehicleSettings.Properties())
                        {
                            var vehicleName = vehicleProperty.Name;
                            var vehicleData = vehicleProperty.Value as Newtonsoft.Json.Linq.JObject;
                            
                            if (vehicleData != null)
                            {
                                var vehicleType = vehicleName.ToLower();
                                _availableCustomVehicles.Add(vehicleType);
                                
                                var prefabPath = vehicleData["Prefab Path"]?.ToString() ?? "N/A";
                                DebugKaruzaVehicles($"Found custom vehicle in Custom Vehicle Settings: {vehicleType} (Prefab: {prefabPath})");
                            }
                        }
                    }
                    else
                    {
                        DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] Custom Vehicle Settings is not a JObject: {config["Custom Vehicle Settings"]?.GetType()}");
                    }
                }
                else
                {
                    DebugKaruzaVehicles($"[CUSTOM VEHICLE DEBUG] 'Custom Vehicle Settings' section not found");
                }
                
                DebugKaruzaVehicles($"Total custom vehicles loaded from vehicle config: {_availableCustomVehicles.Count}");
            }
            catch (Exception ex)
            {
                PrintWarning($"Error checking vehicle config for custom vehicles: {ex.Message}");
                PrintWarning($"Stack trace: {ex.StackTrace}");
            }
        }


        // ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            LoadConfig();
            Init();
        }

        public override void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public override void HarmonyUnload()
        {
            try { OnServerShutdown(); }
            catch (Exception ex) { PrintWarning("HarmonyUnload: " + ex.Message); }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null) continue;
                try { DestroyAllUI(player); }
                catch { }
                ClearImageQueue(player.userID);
            }

            _playerServerPanelView.Clear();
            _playerSelectedCategory.Clear();
            _playerSelectedManageCategory.Clear();
            _playerShopPage.Clear();
            _playerManagePage.Clear();
            _cachedVehicleLists.Clear();
            _cachedVehicleListPlayer.Clear();
            _playerOwnedVehiclesCache.Clear();
            Instance = null;
        }

        // ---- ServerPanel consumer hooks (ServerPanel broadcasts these to registered mods) ----
        internal void OnServerPanelClosed(BasePlayer player)
        {
            if (player == null) return;
            var userId = player.userID;
            ClearImageQueue(userId);
            ClearVehicleListCache(userId);
            _playerServerPanelView.Remove(userId);
            _playerShopPage.Remove(userId);
            _playerManagePage.Remove(userId);
            _playerOwnedVehiclesCache.Remove(userId);
            DebugServerPanel($"[SERVERPANEL] OnServerPanelClosed: cleared state for {userId}");
        }

        /// <summary>
        /// ServerPanel passes the category as an int id (or whatever the caller had), so this takes object.
        /// It must stay void: ServerPanel treats any non-null hook result as "cancel the page switch".
        /// </summary>
        internal void OnServerPanelCategoryPage(BasePlayer player, object category, int page)
        {
            if (player == null) return;
            ClearImageQueue(player.userID);
            DebugServerPanel($"[SERVERPANEL] OnServerPanelCategoryPage: category='{category}' page={page} for {player.userID}");
        }
        #endregion

        #region Chat Commands
       
		private void CmdOpenGUI(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

			if (CorePlugin == null)
            {
				player.ChatMessage(Lang("CorePluginNotLoaded", player));
                return;
            }

			if (!HasCoreUsePermission(player))
            {
                player.ChatMessage(Lang("NoPermission", player));
                return;
            }

            ShowMainGUI(player);
        }

        #endregion

        #region Console Commands

        [ConsoleCommand("vgui.main")]
        private void CmdShowMain(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            DestroyAllUI(player);
            ShowMainGUI(player);
        }

        [ConsoleCommand("vgui.shop")]
        private void CmdShowShop(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var category = arg.GetString(0, "all");
            var fromMainMenu = string.IsNullOrEmpty(arg.GetString(0));
            
            var isCategoryChange = !fromMainMenu && 
                                   _playerSelectedCategory.TryGetValue(player.userID, out var currentCategory) &&
                                   currentCategory != category;
            
            _playerSelectedCategory[player.userID] = category;
            _playerShopPage[player.userID] = 0;
            
            if (!isCategoryChange)
            {
                ClearVehicleListCache(player.userID);
            }
            
            if (isCategoryChange)
            {
                UpdateShopCategoryButtons(player, category);
                UpdateVehicleGridOnly(player, true, category);
            }
            else
            {
                ShowShopGUI(player, category, fromMainMenu);
            }
        }

        [ConsoleCommand("vgui.manage")]
        private void CmdShowManage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var category = arg.GetString(0, "all");
            var fromMainMenu = string.IsNullOrEmpty(arg.GetString(0));
            
            var isCategoryChange = !fromMainMenu && 
                                   _playerSelectedManageCategory.TryGetValue(player.userID, out var currentCategory) &&
                                   currentCategory != category;
            
            _playerSelectedManageCategory[player.userID] = category;
            _playerManagePage[player.userID] = 0;
            
            if (!isCategoryChange)
            {
                ClearVehicleListCache(player.userID);
            }
            
            if (isCategoryChange)
            {
                UpdateManageCategoryButtons(player, category);
                UpdateVehicleGridOnly(player, false, category);
            }
            else
            {
                ShowManageGUI(player, category, fromMainMenu);
            }
        }

        [ConsoleCommand("vgui.buy")]
        private void CmdBuyVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || player.IsDestroyed || player.net?.connection == null) return;

            var vehicleType = arg.GetString(0);
            if (string.IsNullOrEmpty(vehicleType)) return;

            vehicleType = SanitizeVehicleType(vehicleType);
            if (string.IsNullOrEmpty(vehicleType)) return;

            DestroyAllUI(player);

			if (!HasCoreUsePermission(player))
            {
                player.ChatMessage(Lang("NoPermission", player));
                return;
            }

            if (!HasVehiclePermission(player, vehicleType))
            {
                player.ChatMessage(Lang("VipOnly", player));
                return;
            }

            ClearVehicleListCache(player.userID);
            _playerOwnedVehiclesCache.Remove(player.userID);

            try
            {
                var buyCommand = $"/buy {vehicleType}";
                if (player != null && !player.IsDestroyed && player.net?.connection != null)
                {
                    player.SendConsoleCommand("chat.say", buyCommand);
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[BUY] Error sending command: {ex.Message}");
            }
        }

        [ConsoleCommand("vgui.clearcache")]
        private void ClearConfigCache(ConsoleSystem.Arg arg)
        {
            lock (_configCacheLock)
            {
                _cachedVehicleConfig = null;
                _configCacheTimestamp = DateTime.MinValue;
                arg.ReplyWith("Vehicle config cache cleared.");
            }
        }

        [ConsoleCommand("vgui.spawn")]
        private void CmdSpawnVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || player.IsDestroyed || player.net?.connection == null) return;

            var vehicleType = arg.GetString(0);
            if (string.IsNullOrEmpty(vehicleType)) return;

            vehicleType = SanitizeVehicleType(vehicleType);
            if (string.IsNullOrEmpty(vehicleType)) return;

            DestroyAllUI(player);

			if (!HasCoreUsePermission(player))
            {
                player.ChatMessage(Lang("NoPermission", player));
                return;
            }

            if (!HasVehiclePermission(player, vehicleType))
            {
                player.ChatMessage(Lang("VipOnly", player));
                return;
            }

            try
            {
                var spawnCommand = $"/spawn {vehicleType}";
                if (player != null && !player.IsDestroyed && player.net?.connection != null)
                {
                    player.SendConsoleCommand("chat.say", spawnCommand);
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[SPAWN] Error sending command: {ex.Message}");
            }
        }

        [ConsoleCommand("vgui.recall")]
        private void CmdRecallVehicle(ConsoleSystem.Arg arg)
        {
            DebugUI($"[RECALL] Command received. Args: {arg.Args?.Length ?? 0}");
            if (arg.Args != null && arg.Args.Length > 0)
            {
                DebugUI($"[RECALL] First arg: '{arg.Args[0]}'");
            }
            
            var player = arg.Player();
            if (player == null)
            {
                DebugUI("[RECALL] Player is null");
                return;
            }
            if (player.IsDestroyed || player.net?.connection == null)
            {
                DebugUI($"[RECALL] Player {player.userID} is destroyed or disconnected");
                return;
            }

            var vehicleType = arg.GetString(0);
            DebugUI($"[RECALL] Raw vehicleType from arg: '{vehicleType}'");
            if (string.IsNullOrEmpty(vehicleType))
            {
                DebugUI("[RECALL] VehicleType is null or empty");
                return;
            }

            vehicleType = SanitizeVehicleType(vehicleType);
            DebugUI($"[RECALL] Sanitized vehicleType: '{vehicleType}'");
            if (string.IsNullOrEmpty(vehicleType))
            {
                DebugUI("[RECALL] VehicleType is null or empty after sanitization");
                return;
            }

            DebugUI($"[RECALL] Player {player.userID} attempting to recall vehicle with command: '{vehicleType}'");

            DestroyAllUI(player);

            try
            {
                var recallCommand = $"/recall {vehicleType}";
                DebugUI($"[RECALL] Sending command: '{recallCommand}'");
                if (player != null && !player.IsDestroyed && player.net?.connection != null)
                {
                    player.SendConsoleCommand("chat.say", recallCommand);
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[RECALL] Error sending command: {ex.Message}");
            }
        }

        [ConsoleCommand("vgui.pickup")]
        private void CmdPickupVehicle(ConsoleSystem.Arg arg)
        {
            DebugUI($"[PICKUP] Command received. Args: {arg.Args?.Length ?? 0}");
            if (arg.Args != null && arg.Args.Length > 0)
            {
                DebugUI($"[PICKUP] First arg: '{arg.Args[0]}'");
            }
            
            var player = arg.Player();
            if (player == null)
            {
                DebugUI("[PICKUP] Player is null");
                return;
            }
            if (player.IsDestroyed || player.net?.connection == null)
            {
                DebugUI($"[PICKUP] Player {player.userID} is destroyed or disconnected");
                return;
            }

            if (RustVehicles == null || !RustVehicles.IsLoaded)
            {
                player.ChatMessage(Lang("PickupRequiresRustVehicles", player));
                return;
            }

            var vehicleType = arg.GetString(0);
            DebugUI($"[PICKUP] Raw vehicleType from arg: '{vehicleType}'");
            if (string.IsNullOrEmpty(vehicleType))
            {
                DebugUI("[PICKUP] VehicleType is null or empty");
                return;
            }

            vehicleType = SanitizeVehicleType(vehicleType);
            DebugUI($"[PICKUP] Sanitized vehicleType: '{vehicleType}'");
            if (string.IsNullOrEmpty(vehicleType))
            {
                DebugUI("[PICKUP] VehicleType is null or empty after sanitization");
                return;
            }

            DebugUI($"[PICKUP] Player {player.userID} attempting to pickup vehicle: '{vehicleType}'");

            DestroyAllUI(player);

            try
            {
                var pickupCommand = "/pickup";
                DebugUI($"[PICKUP] Sending command: '{pickupCommand}'");
                if (player != null && !player.IsDestroyed && player.net?.connection != null)
                {
                    player.SendConsoleCommand("chat.say", pickupCommand);
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[PICKUP] Error sending command: {ex.Message}");
            }
        }

        [ConsoleCommand("vgui.kill")]
        private void CmdKillVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || player.IsDestroyed || player.net?.connection == null) return;

            var vehicleType = arg.GetString(0);
            if (string.IsNullOrEmpty(vehicleType)) return;

            vehicleType = SanitizeVehicleType(vehicleType);
            if (string.IsNullOrEmpty(vehicleType)) return;

            DestroyAllUI(player);

            try
            {
                var killCommand = $"/kill {vehicleType}";
                if (player != null && !player.IsDestroyed && player.net?.connection != null)
                {
                    player.SendConsoleCommand("chat.say", killCommand);
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[KILL] Error sending command: {ex.Message}");
            }
        }

        [ConsoleCommand("vgui.close")]
        private void CmdCloseGUI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            DestroyAllUI(player);
        }

        [ConsoleCommand("vgui.setcolor")]
        private void CmdSetColor(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var colorName = arg.GetString(0);
            if (string.IsNullOrEmpty(colorName)) return;

            colorName = colorName.ToLower();
            var validColors = new[] { "blue", "green", "purple", "red", "default" };
            if (!Array.Exists(validColors, c => c == colorName))
            {
                DebugUI($"[COLOR] Invalid color name: {colorName}");
                return;
            }

            LoadPlayerSettings(player.userID);

            if (colorName == "default")
            {
                _playerBackgroundColor.Remove(player.userID);
                if (_playerSettings.ContainsKey(player.userID))
                {
                    _playerSettings[player.userID].BackgroundColor = null;
                }
                DebugUI($"[COLOR] Player {player.userID} reset to default background");
            }
            else
            {
                _playerBackgroundColor[player.userID] = colorName;
                if (!_playerSettings.ContainsKey(player.userID))
                {
                    _playerSettings[player.userID] = new PlayerUISettings { UserID = player.userID };
                }
                _playerSettings[player.userID].BackgroundColor = colorName;
                DebugUI($"[COLOR] Player {player.userID} set background color to: {colorName}");
            }
            
            SavePlayerSettings(player.userID);
            
            ShowMainGUI(player);
        }

        [ConsoleCommand("vgui.transparency")]
        private void CmdSetTransparency(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var direction = arg.GetString(0);
            if (string.IsNullOrEmpty(direction)) return;

            direction = direction.ToLower();
            var currentTransparency = GetPlayerTransparency(player.userID);
            var step = 5f;
            
            LoadPlayerSettings(player.userID);
            
            if (direction == "increase")
            {
                var newTransparency = Math.Min(100f, currentTransparency + step);
                _playerTransparency[player.userID] = newTransparency;
                if (!_playerSettings.ContainsKey(player.userID))
                {
                    _playerSettings[player.userID] = new PlayerUISettings { UserID = player.userID };
                }
                _playerSettings[player.userID].Transparency = newTransparency;
                DebugUI($"[TRANSPARENCY] Player {player.userID} increased transparency to: {newTransparency}%");
            }
            else if (direction == "decrease")
            {
                var newTransparency = Math.Max(5f, currentTransparency - step);
                _playerTransparency[player.userID] = newTransparency;
                if (!_playerSettings.ContainsKey(player.userID))
                {
                    _playerSettings[player.userID] = new PlayerUISettings { UserID = player.userID };
                }
                _playerSettings[player.userID].Transparency = newTransparency;
                DebugUI($"[TRANSPARENCY] Player {player.userID} decreased transparency to: {newTransparency}%");
            }
            else
            {
                DebugUI($"[TRANSPARENCY] Invalid direction: {direction}");
                return;
            }
            
            SavePlayerSettings(player.userID);
            
            ShowMainGUI(player);
        }

        [ConsoleCommand("vgui.nextpage")]
        private void CmdNextPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var category = arg.GetString(0, "all");
            if (string.IsNullOrEmpty(category))
            {
                category = _playerSelectedCategory.GetValueOrDefault(player.userID, "all");
            }
            
            var cacheKey = $"{player.userID}_{category}";
            List<VehicleDisplayInfo> vehicles;
            if (!_cachedVehicleLists.TryGetValue(cacheKey, out vehicles) || 
                !_cachedVehicleListPlayer.TryGetValue(cacheKey, out var cachedPlayerId) || 
                cachedPlayerId != player.userID)
            {
                if (_config.EnableUIDebug)
                    DebugUI($"[CACHE] Cache miss in CmdNextPage for {cacheKey}, processing vehicles");
                vehicles = GetAvailableVehicles(player, category);
                _cachedVehicleLists[cacheKey] = vehicles;
                _cachedVehicleListPlayer[cacheKey] = player.userID;
            }
            else
            {
                if (_config.EnableUIDebug)
                    DebugUI($"[CACHE] Cache hit in CmdNextPage for {cacheKey}, using cached list ({vehicles.Count} vehicles)");
            }
            
            var totalPages = Mathf.CeilToInt((float)vehicles.Count / VEHICLES_PER_PAGE);
            var currentPage = _playerShopPage.GetValueOrDefault(player.userID, 0);
            
            if (currentPage < totalPages - 1)
            {
                _playerShopPage[player.userID] = currentPage + 1;
                UpdateVehicleGridOnly(player, true, category);
            }
        }

        [ConsoleCommand("vgui.prevpage")]
        private void CmdPrevPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var category = arg.GetString(0, "all");
            if (string.IsNullOrEmpty(category))
            {
                category = _playerSelectedCategory.GetValueOrDefault(player.userID, "all");
            }
            
            var currentPage = _playerShopPage.GetValueOrDefault(player.userID, 0);
            
            if (currentPage > 0)
            {
                _playerShopPage[player.userID] = Math.Max(0, currentPage - 1);
                UpdateVehicleGridOnly(player, true, category);
            }
        }

        [ConsoleCommand("vgui.manage.nextpage")]
        private void CmdManageNextPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var category = arg.GetString(0);
            if (string.IsNullOrEmpty(category))
            {
                category = _playerSelectedManageCategory.GetValueOrDefault(player.userID, "all");
            }
            
            var allOwnedVehicles = GetOwnedVehicles(player);
            var ownedVehicles = new List<VehicleDisplayInfo>();
            foreach (var vehicle in allOwnedVehicles)
            {
                if (category == "all" || vehicle.Category == category)
                {
                    ownedVehicles.Add(vehicle);
                }
            }
            var totalPages = Math.Max(1, Mathf.CeilToInt((float)ownedVehicles.Count / VEHICLES_PER_PAGE));
            var currentPage = _playerManagePage.GetValueOrDefault(player.userID, 0);
            
            if (currentPage < totalPages - 1)
            {
                _playerManagePage[player.userID] = currentPage + 1;
                UpdateVehicleGridOnly(player, false, category);
            }
        }

        [ConsoleCommand("vgui.manage.prevpage")]
        private void CmdManagePrevPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var category = arg.GetString(0);
            if (string.IsNullOrEmpty(category))
            {
                category = _playerSelectedManageCategory.GetValueOrDefault(player.userID, "all");
            }
            
            var currentPage = _playerManagePage.GetValueOrDefault(player.userID, 0);
            
            if (currentPage > 0)
            {
                _playerManagePage[player.userID] = Math.Max(0, currentPage - 1);
                UpdateVehicleGridOnly(player, false, category);
            }
        }

        [ConsoleCommand("vgui.imgdump")]
        private void CmdDumpImages(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            var count = _imageKeyToPngId.Count;
            DebugUI($"[IMG] Registry contains {count} keys");
            int shown = 0;
            foreach (var kvp in _imageKeyToPngId)
            {
                DebugUI($"[IMG] {kvp.Key} -> {kvp.Value}");
                if (++shown >= 50) break;
            }
            player.ChatMessage(Lang("ImageRegistry", player, count));
        }
        
        #region ServerPanel Commands

        [ConsoleCommand("vgui.serverpanel.view")]
        private void CmdServerPanelView(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                DebugServerPanel("[SERVERPANEL] CmdServerPanelView: Player is null");
                return;
            }

            var view = arg.GetString(0, "main");
            DebugServerPanel($"[SERVERPANEL] CmdServerPanelView: Player {player.userID} switching to view '{view}'");
            _playerServerPanelView[player.userID] = view;
            
            if (view == "shop")
            {
                _playerShopPage[player.userID] = 0;
            }
            else if (view == "manage")
            {
                _playerManagePage[player.userID] = 0;
            }
            
            RefreshServerPanelContent(player);
        }

        [ConsoleCommand("vgui.serverpanel.shop")]
        private void CmdServerPanelShowShop(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                DebugServerPanel("[SERVERPANEL] CmdServerPanelShowShop: Player is null");
                return;
            }

            var category = arg.GetString(0, "all");
            DebugServerPanel($"[SERVERPANEL] CmdServerPanelShowShop: Player {player.userID} selected category '{category}'");
            _playerServerPanelView[player.userID] = "shop";
            _playerSelectedCategory[player.userID] = category;
            _playerShopPage[player.userID] = 0;
            RefreshServerPanelContent(player);
        }

        [ConsoleCommand("vgui.serverpanel.nextpage")]
        private void CmdServerPanelNextPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                DebugServerPanel("[SERVERPANEL] CmdServerPanelNextPage: Player is null");
                return;
            }

            var category = arg.GetString(0, "all");
            var currentPage = _playerShopPage.GetValueOrDefault(player.userID, 0);

            List<VehicleDisplayInfo> vehicles;
            var cacheKey = $"{player.userID}_{category}";
            if (_cachedVehicleLists.TryGetValue(cacheKey, out vehicles) && 
                _cachedVehicleListPlayer.TryGetValue(cacheKey, out var cachedPlayerId) && 
                cachedPlayerId == player.userID)
            {
                DebugServerPanel($"[SERVERPANEL] Cache hit in CmdServerPanelNextPage for {cacheKey}, using cached list ({vehicles.Count} vehicles)");
            }
            else
            {
                DebugServerPanel($"[SERVERPANEL] Cache miss in CmdServerPanelNextPage for {cacheKey}, processing vehicles");
                vehicles = GetAvailableVehicles(player, category);
                _cachedVehicleLists[cacheKey] = vehicles;
                _cachedVehicleListPlayer[cacheKey] = player.userID;
            }
            var totalPages = Math.Max(1, Mathf.CeilToInt((float)vehicles.Count / VEHICLES_PER_PAGE));
            DebugServerPanel($"[SERVERPANEL] CmdServerPanelNextPage: Player {player.userID}, category '{category}', page {currentPage + 1}/{totalPages}");
            if (currentPage < totalPages - 1)
            {
                _playerShopPage[player.userID] = currentPage + 1;
                DebugServerPanel($"[SERVERPANEL] CmdServerPanelNextPage: Moved to page {currentPage + 2}");
            }
            else
            {
                DebugServerPanel($"[SERVERPANEL] CmdServerPanelNextPage: Already on last page");
            }

            RefreshServerPanelContent(player);
        }

        [ConsoleCommand("vgui.serverpanel.prevpage")]
        private void CmdServerPanelPrevPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                DebugServerPanel("[SERVERPANEL] CmdServerPanelPrevPage: Player is null");
                return;
            }

            var category = arg.GetString(0, "all");
            var currentPage = _playerShopPage.GetValueOrDefault(player.userID, 0);
            var newPage = Math.Max(0, currentPage - 1);
            DebugServerPanel($"[SERVERPANEL] CmdServerPanelPrevPage: Player {player.userID}, category '{category}', page {currentPage + 1} -> {newPage + 1}");
            _playerShopPage[player.userID] = newPage;
            RefreshServerPanelContent(player);
        }

        [ConsoleCommand("vgui.serverpanel.manage.category")]
        private void CmdServerPanelManageCategory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                DebugServerPanel("[SERVERPANEL] CmdServerPanelManageCategory: Player is null");
                return;
            }

            var category = arg.GetString(0, "all");
            DebugServerPanel($"[SERVERPANEL] CmdServerPanelManageCategory: Player {player.userID} selected category '{category}'");
            _playerSelectedManageCategory[player.userID] = category;
            _playerManagePage[player.userID] = 0;
            RefreshServerPanelContent(player);
        }

        [ConsoleCommand("vgui.serverpanel.manage.nextpage")]
        private void CmdServerPanelManageNextPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                DebugServerPanel("[SERVERPANEL] CmdServerPanelManageNextPage: Player is null");
                return;
            }

            var category = arg.GetString(0, "all");
            var currentPage = _playerManagePage.GetValueOrDefault(player.userID, 0);

            var allOwnedVehicles = GetOwnedVehicles(player);
            var ownedVehicles = new List<VehicleDisplayInfo>();
            foreach (var vehicle in allOwnedVehicles)
            {
                if (category == "all" || vehicle.Category == category)
                {
                    ownedVehicles.Add(vehicle);
                }
            }
            var totalPages = Math.Max(1, Mathf.CeilToInt((float)ownedVehicles.Count / VEHICLES_PER_PAGE));
            DebugServerPanel($"[SERVERPANEL] CmdServerPanelManageNextPage: Player {player.userID}, category '{category}', page {currentPage + 1}/{totalPages}");
            if (currentPage < totalPages - 1)
            {
                _playerManagePage[player.userID] = currentPage + 1;
                DebugServerPanel($"[SERVERPANEL] CmdServerPanelManageNextPage: Moved to page {currentPage + 2}");
            }
            else
            {
                DebugServerPanel($"[SERVERPANEL] CmdServerPanelManageNextPage: Already on last page");
            }

            RefreshServerPanelContent(player);
        }

        [ConsoleCommand("vgui.serverpanel.manage.prevpage")]
        private void CmdServerPanelManagePrevPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                DebugServerPanel("[SERVERPANEL] CmdServerPanelManagePrevPage: Player is null");
                return;
            }

            var category = arg.GetString(0, "all");
            var currentPage = _playerManagePage.GetValueOrDefault(player.userID, 0);
            var newPage = Math.Max(0, currentPage - 1);
            DebugServerPanel($"[SERVERPANEL] CmdServerPanelManagePrevPage: Player {player.userID}, category '{category}', page {currentPage + 1} -> {newPage + 1}");
            _playerManagePage[player.userID] = newPage;
            RefreshServerPanelContent(player);
        }

        private void RefreshServerPanelContent(BasePlayer player)
        {
            DebugServerPanel($"[SERVERPANEL] RefreshServerPanelContent: player={player?.userID}, ServerPanel loaded={ServerPanel?.IsLoaded ?? false}");
            
            if (player != null)
            {
                ClearImageQueue(player.userID);
                DebugServerPanel($"[SERVERPANEL] RefreshServerPanelContent: Cleared image queue for player {player.userID}");
            }
            
            if (ServerPanel != null && ServerPanel.IsLoaded)
            {
                NextTick(() =>
                {
                    if (player == null || player.IsDestroyed || player.net?.connection == null)
                    {
                        DebugServerPanel("[SERVERPANEL] RefreshServerPanelContent: Player no longer valid");
                        return;
                    }
                    
                    DebugServerPanel($"[SERVERPANEL] RefreshServerPanelContent: Refreshing ServerPanel page 0 (internal page: {_playerShopPage.GetValueOrDefault(player.userID, 0) + 1})");
                    
                    RustVehiclesGUIHost.RunPlayerConsoleCommand(player, "UI_ServerPanel", "menu", "page", "0");
                });
            }
            else
            {
                DebugServerPanel("[SERVERPANEL] RefreshServerPanelContent: ServerPanel not available or not loaded");
            }
        }

        #endregion
        
        #endregion

        #region UI Creation

        private void ShowMainGUI(BasePlayer player)
        {
            DestroyAllUI(player);
            
            var elements = new List<CuiElement>();

            var bgImage = GetPlayerBackgroundImage(player.userID);
            var mainPanel = new ImageSettings
            {
                AnchorMin = "0.35 0.35",
                AnchorMax = "0.65 0.65",
                Color = bgImage != null ? IColor.Create("#FFFFFF", GetPlayerTransparency(player.userID)) : IColor.Create("#1a1a1a", 95f),
                Image = bgImage,
                CursorEnabled = true
            };

            elements.Add(mainPanel.GetImage("Overlay", UI_MAIN));
            DebugUI($"[PANEL DEBUG] Main panel size: {mainPanel.AnchorMin} to {mainPanel.AnchorMax}");

            var titleText = new TextSettings
            {
                AnchorMin = "0 0.9",
                AnchorMax = "1 1",
                FontSize = 20,
                IsBold = true,
                Align = TextAnchor.MiddleCenter,
                Color = IColor.CreateWhite()
            };

            elements.Add(titleText.GetText(Lang("TitleMain", player), UI_MAIN));

            var vehicleCount = GetOwnedVehicleCount(player);
            var maxVehicles = GetMaxVehicles(player);
            var vehicleText = maxVehicles <= 0 ? $"{vehicleCount}" : $"{vehicleCount}/{maxVehicles}";
            
            var topRightInfoText = new TextSettings
            {
                AnchorMin = "0.50 0.75",
                AnchorMax = "0.95 0.87",
                FontSize = 12,
                Align = TextAnchor.UpperRight,
                Color = IColor.CreateWhite()
            };

            elements.Add(topRightInfoText.GetText($"{Lang("VehiclesCount", player, vehicleText)}\n{player.displayName}", UI_MAIN));

            var balanceText = GetPlayerBalance(player);
            var leftInfoText = new TextSettings
            {
                AnchorMin = "0.05 0.75",
                AnchorMax = "0.50 0.87",
                FontSize = 12,
                Align = TextAnchor.UpperLeft,
                Color = IColor.CreateWhite()
            };

            elements.Add(leftInfoText.GetText($"{Lang("Balance", player)}\n{balanceText}", UI_MAIN));

            var buyButtonWidth = 0.30f;
            var manageButtonWidth = 0.38f;
            var buttonHeight = 0.12f;
            var buttonSpacing = 0.05f;
            var centerX = 0.5f;
            var buttonY = 0.40f;
            
            var shopButton = new ButtonSettings
            {
                AnchorMin = $"{centerX - buyButtonWidth - (buttonSpacing / 2) - 0.12} {buttonY - buttonHeight}",
                AnchorMax = $"{centerX - (buttonSpacing / 2) - 0.12} {buttonY}",
                ButtonColor = IColor.CreateTransparent(),
                Color = IColor.CreateWhite(),
                FontSize = 18,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };

            elements.AddRange(shopButton.GetButton(Lang("BuyVehicles", player), "vgui.shop", UI_MAIN));

            var manageButton = new ButtonSettings
            {
                AnchorMin = $"{centerX + (buttonSpacing / 2) + 0.05} {buttonY - buttonHeight}",
                AnchorMax = $"{centerX + manageButtonWidth + (buttonSpacing / 2) + 0.05} {buttonY}",
                ButtonColor = IColor.CreateTransparent(),
                Color = IColor.CreateWhite(),
                FontSize = 18,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };

            elements.AddRange(manageButton.GetButton(Lang("ManageVehicles", player), "vgui.manage", UI_MAIN));

            var closeButton = new ButtonSettings
            {
                AnchorMin = "0.85 0.92",
                AnchorMax = "0.98 0.98",
                ButtonColor = IColor.Create("#d9534f"),
                Color = IColor.CreateWhite(),
                FontSize = 12,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };

            elements.AddRange(closeButton.GetButton("✖", "vgui.close", UI_MAIN));

            var colorBoxSize = 0.06f;
            var colorBoxSpacing = 0.01f;
            var colorBoxStartX = 0.02f;
            var colorBoxStartY = 0.02f;
            
            var colors = new[] { "blue", "green", "purple", "red", "default" };
            
            for (int i = 0; i < colors.Length; i++)
            {
                var colorName = colors[i];
                var boxX = colorBoxStartX + (i * (colorBoxSize + colorBoxSpacing));
                var boxY = colorBoxStartY;
                
                var boxName = $"colorpicker_{colorName}";
                
                if (colorName == "default")
                {
                    var defaultBox = new ImageSettings
                    {
                        AnchorMin = $"{boxX} {boxY}",
                        AnchorMax = $"{boxX + colorBoxSize} {boxY + colorBoxSize}",
                        Color = IColor.Create("#000000"),
                        Image = null
                    };
                    elements.Add(defaultBox.GetImage(UI_MAIN, boxName));
                }
                else
                {
                    var gradientImage = GetGradientImage(colorName);
                    
                    var colorPreviewBox = new ImageSettings
                    {
                        AnchorMin = $"{boxX} {boxY}",
                        AnchorMax = $"{boxX + colorBoxSize} {boxY + colorBoxSize}",
                        Color = IColor.CreateWhite(),
                        Image = gradientImage
                    };
                    
                    elements.Add(colorPreviewBox.GetImage(UI_MAIN, boxName));
                }
                
                var colorButton = new ButtonSettings
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1",
                    ButtonColor = IColor.CreateTransparent(),
                    Color = IColor.CreateTransparent()
                };
                elements.AddRange(colorButton.GetButton("", $"vgui.setcolor {colorName}", boxName));
            }

            var transparencyBoxX = colorBoxStartX + (colors.Length * (colorBoxSize + colorBoxSpacing));
            var transparencyBoxY = colorBoxStartY;
            var transparencyBoxSize = colorBoxSize;
            
            var transparencyPanel = new ImageSettings
            {
                AnchorMin = $"{transparencyBoxX} {transparencyBoxY}",
                AnchorMax = $"{transparencyBoxX + transparencyBoxSize} {transparencyBoxY + transparencyBoxSize}",
                Color = IColor.Create("#333333", 80f)
            };
            var transparencyPanelName = "transparency_control";
            elements.Add(transparencyPanel.GetImage(UI_MAIN, transparencyPanelName));
            
            var decreaseButton = new ButtonSettings
            {
                AnchorMin = "0.02 0.15",
                AnchorMax = "0.48 0.85",
                ButtonColor = IColor.Create("#666666"),
                Color = IColor.CreateWhite(),
                FontSize = 16,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };
            elements.AddRange(decreaseButton.GetButton("-", "vgui.transparency decrease", transparencyPanelName));
            
            var increaseButton = new ButtonSettings
            {
                AnchorMin = "0.52 0.15",
                AnchorMax = "0.98 0.85",
                ButtonColor = IColor.Create("#666666"),
                Color = IColor.CreateWhite(),
                FontSize = 16,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };
            elements.AddRange(increaseButton.GetButton("+", "vgui.transparency increase", transparencyPanelName));

            if (player != null && !player.IsDestroyed && player.net?.connection != null)
            {
                try
                {
                    CuiHelper.AddUi(player, elements);
                }
                catch (Exception ex)
                {
                    DebugUI($"[UI] Error adding main UI: {ex.Message}");
                }
            }
        }

        private void ShowShopGUI(BasePlayer player, string category = "all", bool fromMainMenu = false)
        {
            if (fromMainMenu)
            {
                DestroyAllUI(player);
            }
            else
            {
                DestroyUI(player, UI_SHOP);
            }
            
            var elements = new List<CuiElement>();

            var bgImage = GetPlayerBackgroundImage(player.userID);
            var shopPanel = new ImageSettings
            {
                AnchorMin = "0.15 0.15",
                AnchorMax = "0.85 0.95",  
                Color = bgImage != null ? IColor.Create("#FFFFFF", GetPlayerTransparency(player.userID)) : IColor.Create("#1a1a1a", 95f),
                Image = bgImage,
                CursorEnabled = true
            };

            elements.Add(shopPanel.GetImage("Overlay", UI_SHOP));
            DebugUI($"[PANEL DEBUG] Shop panel size: {shopPanel.AnchorMin} to {shopPanel.AnchorMax}");

            var titleText = new TextSettings
            {
                AnchorMin = "0.15 0.96",
                AnchorMax = "0.85 0.99",
                FontSize = 18,
                IsBold = true,
                Align = TextAnchor.MiddleCenter,
                Color = IColor.CreateWhite()
            };

            elements.Add(titleText.GetText(Lang("TitleShop", player), UI_SHOP));

            var closeButton = new ButtonSettings
            {
                AnchorMin = "0.88 0.96",
                AnchorMax = "0.93 0.99",
                ButtonColor = IColor.Create("#d9534f"),
                Color = IColor.CreateWhite(),
                FontSize = 12,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };

            elements.AddRange(closeButton.GetButton("✖", "vgui.close", UI_SHOP));

            var backButton = new ButtonSettings
            {
                AnchorMin = "0.02 0.96",
                AnchorMax = "0.1 0.99",
                ButtonColor = IColor.Create("#6c757d"),
                Color = IColor.CreateWhite(),
                FontSize = 12,
                Align = TextAnchor.MiddleCenter
            };

            elements.AddRange(backButton.GetButton(Lang("Back", player), "vgui.main", UI_SHOP));

            var categories = new[] { "all", "air", "land", "water", "train", "siege" };
            var catWidth = 0.08f;
            var catHeight = 0.025f;
            var catY = 0.95f;  
            
            for (int i = 0; i < categories.Length; i++)
            {
                var cat = categories[i];
                var isActive = cat == category;
                
                var catButton = new ButtonSettings
                {
                    AnchorMin = $"{0.02 + (i * (catWidth + 0.01f))} {catY - catHeight}",
                    AnchorMax = $"{0.02 + ((i + 1) * (catWidth + 0.01f)) - 0.01f} {catY}",
                    ButtonColor = IColor.Create(isActive ? "#4a90e2" : "#555555"),
                    Color = IColor.CreateWhite(),
                    FontSize = 8,
                    Align = TextAnchor.MiddleCenter
                };

                var buttonElements = catButton.GetButton(LangCategory(player, cat), $"vgui.shop {cat}", UI_SHOP, $"{UI_SHOP}_cat_{cat}");
                elements.AddRange(buttonElements);
            }

            var vehicles = GetAvailableVehicles(player, category);
            var cacheKey = $"{player.userID}_{category}";
            _cachedVehicleLists[cacheKey] = vehicles;
            _cachedVehicleListPlayer[cacheKey] = player.userID;
            if (_config.EnableUIDebug)
                DebugUI($"[CACHE] Populated cache in ShowShopGUI for {cacheKey} ({vehicles.Count} vehicles)");
            
            var currentPage = _playerShopPage.GetValueOrDefault(player.userID, 0);
            CreateVehicleGrid(elements, UI_SHOP, vehicles, true, player, currentPage, category);

            if (player != null && !player.IsDestroyed && player.net?.connection != null)
            {
                try
                {
                    CuiHelper.AddUi(player, elements);
                    QueuePageImages(player, vehicles, currentPage, true);
                }
                catch (Exception ex)
                {
                    DebugUI($"[UI] Error adding shop UI: {ex.Message}");
                }
            }
        }

        private void ShowManageGUI(BasePlayer player, string category = "all", bool fromMainMenu = false)
        {
            if (fromMainMenu)
            {
                DestroyAllUI(player);
            }
            else
            {
                DestroyUI(player, UI_MANAGE);
            }
            
            var elements = new List<CuiElement>();

            var bgImage = GetPlayerBackgroundImage(player.userID);
            var managePanel = new ImageSettings
            {
                AnchorMin = "0.13 0.15",
                AnchorMax = "0.90 0.95",
                Color = bgImage != null ? IColor.Create("#FFFFFF", GetPlayerTransparency(player.userID)) : IColor.Create("#1a1a1a", 95f),
                Image = bgImage,
                CursorEnabled = true
            };

            elements.Add(managePanel.GetImage("Overlay", UI_MANAGE));
            DebugUI($"[PANEL DEBUG] Manage panel size: {managePanel.AnchorMin} to {managePanel.AnchorMax}");

            var titleText = new TextSettings
            {
                AnchorMin = "0.15 0.96",
                AnchorMax = "0.85 0.99",
                FontSize = 18,
                IsBold = true,
                Align = TextAnchor.MiddleCenter,
                Color = IColor.CreateWhite()
            };

            elements.Add(titleText.GetText(Lang("TitleManage", player), UI_MANAGE));

            var closeButton = new ButtonSettings
            {
                AnchorMin = "0.88 0.96", 
                AnchorMax = "0.93 0.99",
                ButtonColor = IColor.Create("#d9534f"),
                Color = IColor.CreateWhite(),
                FontSize = 12,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };

            elements.AddRange(closeButton.GetButton("✖", "vgui.close", UI_MANAGE));

            var backButton = new ButtonSettings
            {
                AnchorMin = "0.02 0.96",
                AnchorMax = "0.1 0.99",
                ButtonColor = IColor.Create("#6c757d"),
                Color = IColor.CreateWhite(),
                FontSize = 12,
                Align = TextAnchor.MiddleCenter
            };

            elements.AddRange(backButton.GetButton(Lang("Back", player), "vgui.main", UI_MANAGE));

            var categories = new[] { "all", "air", "land", "water", "train", "siege" };
            var catWidth = 0.08f;
            var catHeight = 0.025f;
            var catY = 0.95f;  
            
            for (int i = 0; i < categories.Length; i++)
            {
                var cat = categories[i];
                var isActive = cat == category;
                
                var catButton = new ButtonSettings
                {
                    AnchorMin = $"{0.02 + (i * (catWidth + 0.01f))} {catY - catHeight}",
                    AnchorMax = $"{0.02 + ((i + 1) * (catWidth + 0.01f)) - 0.01f} {catY}",
                    ButtonColor = IColor.Create(isActive ? "#4a90e2" : "#555555"),
                    Color = IColor.CreateWhite(),
                    FontSize = 8,
                    Align = TextAnchor.MiddleCenter
                };

                var buttonElements = catButton.GetButton(LangCategory(player, cat), $"vgui.manage {cat}", UI_MANAGE, $"{UI_MANAGE}_cat_{cat}");
                elements.AddRange(buttonElements);
            }

            var ownedVehicles = GetOwnedVehicles(player, category);
            var currentPage = _playerManagePage.GetValueOrDefault(player.userID, 0);
            CreateVehicleGrid(elements, UI_MANAGE, ownedVehicles, false, player, currentPage, category);

            if (player != null && !player.IsDestroyed && player.net?.connection != null)
            {
                try
                {
                    CuiHelper.AddUi(player, elements);
                    QueuePageImages(player, ownedVehicles, currentPage, false);
                }
                catch (Exception ex)
                {
                    DebugUI($"[UI] Error adding manage UI: {ex.Message}");
                }
            }
        }

		private void CreateVehicleGrid(List<CuiElement> elements, string parent, List<VehicleDisplayInfo> vehicles, bool isShop, BasePlayer player, int page = 0, string category = "")
        {
            if (vehicles == null || vehicles.Count == 0)
            {
                var noVehiclesText = new TextSettings
                {
                    AnchorMin = "0.2 0.4",
                    AnchorMax = "0.8 0.6",
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = IColor.Create("#888888")
                };

                var message = isShop ? Lang("NoVehiclesPurchase", player) : 
                    (category != "all" ? Lang("NoVehiclesCategory", player) : Lang("NoVehiclesYet", player));
                elements.Add(noVehiclesText.GetText(message, parent));
                return;
            }

            var startIndex = page * VEHICLES_PER_PAGE;
            var pageVehicles = new List<VehicleDisplayInfo>();
            for (int i = startIndex; i < vehicles.Count && pageVehicles.Count < VEHICLES_PER_PAGE; i++)
            {
                pageVehicles.Add(vehicles[i]);
            }
            var totalPages = Mathf.CeilToInt((float)vehicles.Count / VEHICLES_PER_PAGE);

            if (totalPages > 1)
            {
                var pageInfo = new TextSettings
                {
                    AnchorMin = "0.70 0.92",
                    AnchorMax = "0.82 0.95",
                    FontSize = 8,
                    Align = TextAnchor.MiddleCenter,
                    Color = IColor.CreateWhite()
                };
                elements.Add(pageInfo.GetText(Lang("PageOf", player, page + 1, totalPages), parent, $"{parent}_pageinfo"));

                if (page > 0)
                {
                    var prevButton = new ButtonSettings
                    {
                        AnchorMin = "0.82 0.92",
                        AnchorMax = "0.87 0.95",
                        ButtonColor = IColor.Create("#6c757d"),
                        Color = IColor.CreateWhite(),
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter
                    };
                    var prevCommand = isShop ? $"vgui.prevpage {category}" : $"vgui.manage.prevpage {category}";
                    var prevElements = prevButton.GetButton("◀", prevCommand, parent, $"{parent}_prevbutton");
                    elements.AddRange(prevElements);
                }

                if (page < totalPages - 1)
                {
                    var nextButton = new ButtonSettings
                    {
                        AnchorMin = "0.88 0.92",
                        AnchorMax = "0.93 0.95",
                        ButtonColor = IColor.Create("#6c757d"),
                        Color = IColor.CreateWhite(),
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter
                    };
                    var nextCommand = isShop ? $"vgui.nextpage {category}" : $"vgui.manage.nextpage {category}";
                    var nextElements = nextButton.GetButton("▶", nextCommand, parent, $"{parent}_nextbutton");
                    elements.AddRange(nextElements);
                }
            }

            int columns = 4;
            float startX = 0.02f;
            float startY = 0.91f;
            float itemWidth = 0.22f;
            float itemHeight = 0.21f; 
            float spacingX = 0.02f;
            float spacingY = 0.02f;

            for (int i = 0; i < pageVehicles.Count; i++)
            {
                var vehicle = pageVehicles[i];
                int row = i / columns;
                int col = i % columns;

                float x1 = startX + col * (itemWidth + spacingX);
                float y1 = startY - row * (itemHeight + spacingY);
                float x2 = x1 + itemWidth;
                float y2 = y1 - itemHeight;

                var vehiclePanel = new ImageSettings
                {
                    AnchorMin = $"{x1} {y2}",
                    AnchorMax = $"{x2} {y1}",
                    Color = IColor.CreateTransparent()
                };

                elements.Add(vehiclePanel.GetImage(parent, $"vehicle_{i}"));

				var vehicleImage = GetVehicleImage(vehicle.ImageKey, vehicle.DisplayName);
                if (!string.IsNullOrEmpty(vehicleImage))
                {
                }
				else
				{
					var pascalKey = GetPascalImageKeyFromSection(vehicle.DisplayName);
					var suggestion = string.IsNullOrEmpty(pascalKey) ? "NO IMG" : $"{pascalKey}.png";
					var noImageText = new TextSettings
					{
						AnchorMin = "0.1 0.35",
						AnchorMax = "0.9 0.9",
						FontSize = 8,
						Align = TextAnchor.MiddleCenter,
						Color = IColor.Create("#FF6B6B")
					};

					elements.Add(noImageText.GetText(Lang("AddImage", player, suggestion), $"vehicle_{i}"));
				}

                var nameText = new TextSettings
                {
                    AnchorMin = "0.05 0.22",
                    AnchorMax = "0.95 0.32",
                    FontSize = 10,
                    IsBold = true,
                    Align = TextAnchor.MiddleCenter,
                    Color = IColor.CreateWhite()
                };

                elements.Add(nameText.GetText(vehicle.DisplayName, $"vehicle_{i}"));

                var infoText = new TextSettings
                {
                    AnchorMin = "0.05 0.12",
                    AnchorMax = "0.95 0.22",
                    FontSize = 8,
                    Align = TextAnchor.MiddleCenter,
                    Color = isShop ? (vehicle.CanAfford ? IColor.Create("#90EE90") : IColor.Create("#FF6B6B")) : IColor.Create("#87CEEB")
                };

                elements.Add(infoText.GetText(vehicle.StatusInfo, $"vehicle_{i}"));

                if (isShop)
                {
                    var buttonColor = vehicle.CanAfford ? "#5cb85c" : "#d9534f";
                    var actionButton = new ButtonSettings
                    {
                        AnchorMin = "0.40 0.02",
                        AnchorMax = "0.60 0.1",
                        ButtonColor = IColor.Create(buttonColor),
                        Color = IColor.CreateWhite(),
                        FontSize = 8,
                        IsBold = true,
                        Align = TextAnchor.MiddleCenter
                    };
                    elements.AddRange(actionButton.GetButton(Lang("Buy", player), $"vgui.buy {vehicle.VehicleType}", $"vehicle_{i}"));
                }
                else
                {
                    var recallButton = new ButtonSettings
                    {
                        AnchorMin = "0.05 0.02",
                        AnchorMax = "0.32 0.1",
                        ButtonColor = IColor.CreateTransparent(),
                        Color = IColor.CreateWhite(),
                        FontSize = 8,
                        IsBold = true,
                        Align = TextAnchor.MiddleCenter
                    };
                    var recallCommand = $"vgui.recall {vehicle.VehicleType}";
                    DebugUI($"[GRID] Creating RECALL button for vehicle '{vehicle.DisplayName}' (Type: '{vehicle.VehicleType}') with command: '{recallCommand}'");
                    elements.AddRange(recallButton.GetButton(Lang("Recall", player), recallCommand, $"vehicle_{i}"));

                    var pickupButton = new ButtonSettings
                    {
                        AnchorMin = "0.34 0.02",
                        AnchorMax = "0.61 0.1",
                        ButtonColor = IColor.CreateTransparent(),
                        Color = IColor.CreateWhite(),
                        FontSize = 8,
                        IsBold = true,
                        Align = TextAnchor.MiddleCenter
                    };
                    var pickupCommand = $"vgui.pickup {vehicle.VehicleType}";
                    DebugUI($"[GRID] Creating PICKUP button for vehicle '{vehicle.DisplayName}' (Type: '{vehicle.VehicleType}') with command: '{pickupCommand}'");
                    elements.AddRange(pickupButton.GetButton(Lang("Pickup", player), pickupCommand, $"vehicle_{i}"));

                    if (!vehicle.IsSpawned)
                    {
                        var spawnButton = new ButtonSettings
                        {
                            AnchorMin = "0.63 0.02",
                            AnchorMax = "0.95 0.1",
                            ButtonColor = IColor.CreateTransparent(),
                            Color = IColor.CreateWhite(),
                            FontSize = 8,
                            IsBold = true,
                            Align = TextAnchor.MiddleCenter
                        };
                        var spawnCommand = $"vgui.spawn {vehicle.VehicleType}";
                        DebugUI($"[GRID] Creating SPAWN button for vehicle '{vehicle.DisplayName}' (Type: '{vehicle.VehicleType}') with command: '{spawnCommand}'");
                        elements.AddRange(spawnButton.GetButton(Lang("Spawn", player), spawnCommand, $"vehicle_{i}"));
                    }
                }
            }
        }

        #endregion

        #region Helper Methods

        private void DestroyAllUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UI_MAIN);
            CuiHelper.DestroyUi(player, UI_SHOP);
            CuiHelper.DestroyUi(player, UI_MANAGE);
        }

        private void DestroyUI(BasePlayer player, string uiName)
        {
            CuiHelper.DestroyUi(player, uiName);
        }

        private void DestroyVehicleGrid(BasePlayer player, bool isShop)
        {
            if (player == null || player.IsDestroyed) return;

            try
            {
                for (int i = 0; i < VEHICLES_PER_PAGE; i++)
                {
                    CuiHelper.DestroyUi(player, $"vehicle_{i}");
                }

                var parent = isShop ? UI_SHOP : UI_MANAGE;
                CuiHelper.DestroyUi(player, $"{parent}_pageinfo");
                CuiHelper.DestroyUi(player, $"{parent}_prevbutton");
                CuiHelper.DestroyUi(player, $"{parent}_nextbutton");
            }
            catch (Exception ex)
            {
                DebugUI($"[UI] Error destroying vehicle grid: {ex.Message}");
            }
        }

        private void UpdateVehicleGridOnly(BasePlayer player, bool isShop, string category = "all")
        {
            if (player == null || player.IsDestroyed || player.net?.connection == null) return;

            try
            {
                DestroyVehicleGrid(player, isShop);

                List<VehicleDisplayInfo> vehicles;
                if (isShop)
                {
                    var cacheKey = $"{player.userID}_{category}";
                    if (_cachedVehicleLists.TryGetValue(cacheKey, out vehicles) && 
                        _cachedVehicleListPlayer.TryGetValue(cacheKey, out var cachedPlayerId) && 
                        cachedPlayerId == player.userID)
                    {
                        if (_config.EnableUIDebug)
                            DebugUI($"[CACHE] Cache hit in UpdateVehicleGridOnly for {cacheKey}, using cached list ({vehicles.Count} vehicles)");
                    }
                    else
                    {
                        if (_config.EnableUIDebug)
                            DebugUI($"[CACHE] Cache miss in UpdateVehicleGridOnly for {cacheKey}, processing vehicles");
                        vehicles = GetAvailableVehicles(player, category);
                        _cachedVehicleLists[cacheKey] = vehicles;
                        _cachedVehicleListPlayer[cacheKey] = player.userID;
                    }
                }
                else
                {
                    vehicles = GetOwnedVehicles(player, category);
                }
                var parent = isShop ? UI_SHOP : UI_MANAGE;
                var currentPage = isShop 
                    ? _playerShopPage.GetValueOrDefault(player.userID, 0)
                    : _playerManagePage.GetValueOrDefault(player.userID, 0);

                var elements = Facepunch.Pool.Get<List<CuiElement>>();
                try
                {
                    CreateVehicleGrid(elements, parent, vehicles, isShop, player, currentPage, category);

                    if (elements.Count > 0)
                    {
                        CuiHelper.AddUi(player, elements);
                        QueuePageImages(player, vehicles, currentPage, isShop);
                    }
                }
                finally
                {
                    Facepunch.Pool.FreeUnmanaged(ref elements);
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[UI] Error updating vehicle grid: {ex.Message}");
            }
        }

        private void UpdateShopCategoryButtons(BasePlayer player, string activeCategory)
        {
            if (player == null || player.IsDestroyed || player.net?.connection == null) return;

            try
            {
                var categories = new[] { "all", "air", "land", "water", "train", "siege" };
                for (int i = 0; i < categories.Length; i++)
                {
                    CuiHelper.DestroyUi(player, $"{UI_SHOP}_cat_{categories[i]}");
                }

                var elements = Facepunch.Pool.Get<List<CuiElement>>();
                try
                {
                    var catWidth = 0.08f;
                    var catHeight = 0.025f;
                    var catY = 0.95f;
                    
                    for (int i = 0; i < categories.Length; i++)
                    {
                        var cat = categories[i];
                        var isActive = cat == activeCategory;
                        
                        var catButton = new ButtonSettings
                        {
                            AnchorMin = $"{0.02 + (i * (catWidth + 0.01f))} {catY - catHeight}",
                            AnchorMax = $"{0.02 + ((i + 1) * (catWidth + 0.01f)) - 0.01f} {catY}",
                            ButtonColor = IColor.Create(isActive ? "#4a90e2" : "#555555"),
                            Color = IColor.CreateWhite(),
                            FontSize = 8,
                            Align = TextAnchor.MiddleCenter
                        };

                        var buttonElements = catButton.GetButton(LangCategory(player, cat), $"vgui.shop {cat}", UI_SHOP, $"{UI_SHOP}_cat_{cat}");
                        elements.AddRange(buttonElements);
                    }

                    if (elements.Count > 0)
                    {
                        CuiHelper.AddUi(player, elements);
                    }
                }
                finally
                {
                    Facepunch.Pool.FreeUnmanaged(ref elements);
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[UI] Error updating category buttons: {ex.Message}");
            }
        }

        private void UpdateManageCategoryButtons(BasePlayer player, string activeCategory)
        {
            if (player == null || player.IsDestroyed || player.net?.connection == null) return;

            try
            {
                var categories = new[] { "all", "air", "land", "water", "train", "siege" };
                for (int i = 0; i < categories.Length; i++)
                {
                    CuiHelper.DestroyUi(player, $"{UI_MANAGE}_cat_{categories[i]}");
                }

                var elements = Facepunch.Pool.Get<List<CuiElement>>();
                try
                {
                    var catWidth = 0.08f;
                    var catHeight = 0.025f;
                    var catY = 0.95f;
                    
                    for (int i = 0; i < categories.Length; i++)
                    {
                        var cat = categories[i];
                        var isActive = cat == activeCategory;
                        
                        var catButton = new ButtonSettings
                        {
                            AnchorMin = $"{0.02 + (i * (catWidth + 0.01f))} {catY - catHeight}",
                            AnchorMax = $"{0.02 + ((i + 1) * (catWidth + 0.01f)) - 0.01f} {catY}",
                            ButtonColor = IColor.Create(isActive ? "#4a90e2" : "#555555"),
                            Color = IColor.CreateWhite(),
                            FontSize = 8,
                            Align = TextAnchor.MiddleCenter
                        };

                        var buttonElements = catButton.GetButton(LangCategory(player, cat), $"vgui.manage {cat}", UI_MANAGE, $"{UI_MANAGE}_cat_{cat}");
                        elements.AddRange(buttonElements);
                    }

                    if (elements.Count > 0)
                    {
                        CuiHelper.AddUi(player, elements);
                    }
                }
                finally
                {
                    Facepunch.Pool.FreeUnmanaged(ref elements);
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[UI] Error updating manage category buttons: {ex.Message}");
            }
        }

		private void DebugKaruzaVehicles(string message)
		{
			if (_config?.EnableKaruzaVehiclesDebug == true)
				PrintWarning(message);
		}

		private void DebugUI(string message)
		{
			if (_config?.EnableUIDebug == true)
				PrintWarning(message);
		}

		private void DebugServerPanel(string message)
		{
			if (_config?.EnableServerPanelDebug == true)
				PrintWarning(message);
		}

		private string GetPlayerBackgroundImage(ulong playerId)
		{
			LoadPlayerSettings(playerId);
			
			if (_playerBackgroundColor.TryGetValue(playerId, out var colorName))
			{
				return GetGradientImage(colorName);
			}
			return null;
		}

		private float GetPlayerTransparency(ulong playerId)
		{
			LoadPlayerSettings(playerId);
			
			if (_playerTransparency.TryGetValue(playerId, out var transparency))
			{
				return transparency;
			}
			return DEFAULT_TRANSPARENCY;
		}

		private string GetGradientImage(string colorName)
		{
			var imageKey = $"gradient_{colorName}";
			var imageId = GetVehicleImage(imageKey, null);
			if (!string.IsNullOrEmpty(imageId))
			{
				return imageId;
			}
			if (RegisterLocalImageKey(imageKey, out var newId))
			{
				return newId.ToString();
			}
			return null;
		}

		private void RegisterGradientImages()
		{
			var gradientColors = new[] { "blue", "green", "purple", "red" };
			foreach (var color in gradientColors)
			{
				var imageKey = $"gradient_{color}";
				if (!_imageKeyToPngId.ContainsKey(imageKey))
				{
					RegisterLocalImageKey(imageKey, out _);
				}
			}
		}


		private string SanitizeVehicleType(string vehicleType)
		{
			if (string.IsNullOrEmpty(vehicleType))
				return string.Empty;

			var sb = new StringBuilder(vehicleType.Length);
			foreach (char c in vehicleType)
			{
				if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
				{
					sb.Append(c);
				}
			}

			var sanitized = sb.ToString();
			if (sanitized.Length > 64)
				sanitized = sanitized.Substring(0, 64);

			return sanitized;
		}

		private string ToImageKey(string name)
		{
			if (string.IsNullOrEmpty(name)) return string.Empty;
			var sb = new StringBuilder(name.Length);
			for (int i = 0; i < name.Length; i++)
			{
				char c = name[i];
				if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
					sb.Append(char.ToLowerInvariant(c));
			}
			return sb.ToString();
		}

		private string StripVehicleWord(string sectionName)
		{
			if (string.IsNullOrEmpty(sectionName)) return sectionName;
			var suffix = " Vehicle";
			if (sectionName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				return sectionName.Substring(0, sectionName.Length - suffix.Length);
			return sectionName;
		}

		private string GetImageKeyFromSection(string sectionName)
		{
			return ToImageKey(StripVehicleWord(sectionName));
		}

		private static readonly Dictionary<string, string> SpecialTitleToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "water bird", "WaterHeli" },
			{ "patrol helicopter", "PatrolHeli" },
			{ "fighter plane", "HeavyFighter" },
			{ "mars fighter detailed", "MarsFighter" },
			{ "tie fighter detailed", "TinFighter" },
			{ "semi truck red", "SemiTruck_Red" },
			{ "semi truck yellow", "SemiTruck_Yellow" },
			{ "semi truck green", "SemiTruck_Green" },
			{ "semi truck blue", "SemiTruck_Blue" },
			{ "semi truck white", "SemiTruck_White" },
			{ "semi trailer orange", "SemiTrailer_Orange" },
			{ "semi trailer green", "SemiTrailer_Green" },
			{ "semi trailer yellow", "SemiTrailer_Yellow" },
			{ "semi trailer blue", "SemiTrailer_Blue" },
			{ "semi trailer fuel", "SemiTrailer_Fuel" },
			{ "blue shopping cart", "ShoppingCartBlue" },
			{ "hover kart 1", "Kart1" },
			{ "police car", "PoliceCar2" },
			{ "hover racer", "Hovercraft" },
			{ "super bike black", "Superbike_Black" },
			{ "super bike blue", "Superbike_Blue" },
			{ "super bike green", "Superbike_Green" },
			{ "super bike orange", "Superbike_Orange" },
			{ "super bike red", "Superbike_Red" }
		};

		private string GetPascalImageKeyFromSection(string sectionName)
		{
			var stripped = StripVehicleWord(sectionName) ?? string.Empty;
			var sb = new StringBuilder(stripped.Length);
			bool newWord = true;
			for (int i = 0; i < stripped.Length; i++)
			{
				char c = stripped[i];
				bool isAlnum = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
				if (!isAlnum)
				{
					newWord = true;
					continue;
				}
				if (newWord)
				{
					sb.Append(char.ToUpperInvariant(c));
					newWord = false;
				}
				else
				{
					sb.Append(c);
				}
			}
			return sb.ToString();
		}

		private string GetPascalWithUnderscoresImageKey(string sectionName)
		{
			var stripped = StripVehicleWord(sectionName) ?? string.Empty;
			var sb = new StringBuilder(stripped.Length);
			bool newWord = true;
			for (int i = 0; i < stripped.Length; i++)
			{
				char c = stripped[i];
				bool isAlnum = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
				if (!isAlnum)
				{
					if (newWord == false && (c == ' ' || c == '-' || c == '_'))
					{
						sb.Append('_');
					}
					newWord = true;
					continue;
				}
				if (newWord)
				{
					sb.Append(char.ToUpperInvariant(c));
					newWord = false;
				}
				else
				{
					sb.Append(char.ToLowerInvariant(c));
				}
			}
			return sb.ToString();
		}

		private string GetImageKeyForCommand(string commandVehicleType)
		{
			try
			{
				var configPath = GetCoreConfigPath();
				if (!System.IO.File.Exists(configPath))
					return null;

				var jsonContent = System.IO.File.ReadAllText(configPath);
				var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);

				var sectionsToSearch = new[] { "Modular Vehicle Settings", "Normal Vehicle Settings", "Train Vehicle Settings", "Custom Vehicle Settings" };
				foreach (var topKey in sectionsToSearch)
				{
					if (!config.ContainsKey(topKey) || !(config[topKey] is Newtonsoft.Json.Linq.JObject topObj))
						continue;

					foreach (var prop in topObj.Properties())
					{
						var titleKey = GetImageKeyFromSection(prop.Name);
						if (string.Equals(titleKey, ToImageKey(commandVehicleType), StringComparison.OrdinalIgnoreCase))
							return titleKey;
					}
				}

				return null;
			}
			catch
			{
				return null;
			}
		}

        private void QueuePageImages(BasePlayer player, List<VehicleDisplayInfo> all, int pageIndex, bool isShop)
        {
            ClearImageQueue(player.userID);

            if (all == null || all.Count == 0)
                return;

            var start = pageIndex * VEHICLES_PER_PAGE;
            var end = Math.Min(start + VEHICLES_PER_PAGE, all.Count);
            var q = new Queue<ImageTask>();

            DebugUI($"[IMGQ] QueuePageImages page={pageIndex} count={end - start} isShop={isShop}");
            for (int i = start; i < end; i++)
            {
                var vehicle = all[i];
                q.Enqueue(new ImageTask
                {
                    Parent = $"vehicle_{i - start}",
                    AnchorMin = "0.1 0.35",
                    AnchorMax = "0.9 0.9",
                    ImageKey = vehicle.ImageKey,
                    ImageTitle = vehicle.DisplayName
                });
            }

            _imageQueues[player.userID] = q;
            _imageQueueActive.Add(player.userID);
            timer.Once(0.5f, () => ProcessImageQueue(player.userID));
        }

        private void ProcessImageQueue(ulong userId)
        {
            if (!_imageQueueActive.Contains(userId)) return;

            var player = BasePlayer.FindByID(userId);
            if (player == null || player.IsDestroyed || player.net?.connection == null)
            {
                ClearImageQueue(userId);
                return;
            }

            if (!_imageQueues.TryGetValue(userId, out var q) || q.Count == 0)
            {
                ClearImageQueue(userId);
                return;
            }

            var maxBatchSize = Math.Min(VEHICLES_PER_PAGE, _config?.ImageThrottle?.BatchSize ?? 4);
            var batchCount = Math.Max(1, maxBatchSize);
            var list = Facepunch.Pool.Get<List<CuiElement>>();

            try
            {
                for (int i = 0; i < batchCount && q.Count > 0; i++)
                {
                    var task = q.Dequeue();
                    var imgData = GetVehicleImage(task.ImageKey, task.ImageTitle);
                    if (string.IsNullOrEmpty(imgData))
                        continue;

                    var elem = new CuiElement
                    {
                        Parent = task.Parent,
                        Components = { new CuiRectTransformComponent { AnchorMin = task.AnchorMin, AnchorMax = task.AnchorMax } }
                    };
                    
                    if (uint.TryParse(imgData, out var pngId) && pngId != 0)
                    {
                        var ownerId = CommunityEntity.ServerInstance?.net?.ID ?? default(NetworkableId);
                        if (ownerId != default(NetworkableId) && FileStorage.server.Get(pngId, FileStorage.Type.png, ownerId, 0u) != null)
                        {
                            elem.Components.Add(new CuiRawImageComponent { Png = pngId.ToString(), Color = IColor.CreateWhite().Get() });
                            DebugUI($"[IMGQ] Render pngId={pngId}");
                        }
                        else
                        {
                            DebugUI($"[IMGQ] Invalid PNG ID {pngId}, skipping");
                            continue;
                        }
                    }
                    else if (!string.IsNullOrEmpty(imgData))
                    {
                        if (IsURL(imgData))
                        {
                            elem.Components.Add(new CuiRawImageComponent { Url = imgData, Color = IColor.CreateWhite().Get() });
                            DebugUI("[IMGQ] Render URL image");
                        }
                        else
                        {
                            DebugUI($"[IMGQ] Skipping invalid image data (not URL or valid PNG ID): {imgData.Substring(0, Math.Min(50, imgData.Length))}");
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }
                    
                    list.Add(elem);
                }

                if (list.Count > 0 && player != null && !player.IsDestroyed && player.net?.connection != null)
                {
                    try
                    {
                        CuiHelper.AddUi(player, list);
                        DebugUI($"[IMGQ] Successfully added {list.Count} image elements");
                    }
                    catch (Exception ex)
                    {
                        DebugUI($"[IMGQ] Error adding UI (may be temporary): {ex.Message}");
                        if (!ex.Message.Contains("Unknown Parent"))
                        {
                            ClearImageQueue(userId);
                        }
                    }
                }
            }
            finally
            {
                Facepunch.Pool.FreeUnmanaged(ref list);
            }

            if (q.Count > 0 && _imageQueueActive.Contains(userId))
            {
                var dt = Math.Max(0.15f, _config?.ImageThrottle?.IntervalSeconds ?? 0.15f);
                timer.Once(dt, () => ProcessImageQueue(userId));
            }
            else
            {
                ClearImageQueue(userId);
            }
        }

        private void ClearImageQueue(ulong userId)
        {
            _imageQueueActive.Remove(userId);
            if (_imageQueues.TryGetValue(userId, out var q))
                q.Clear();
            _imageQueues.Remove(userId);
            DebugUI($"[IMGQ] Cleared queue for {userId}");
        }

		private Plugin CorePlugin
		{
			get
			{
				if (_corePluginCacheValid && _cachedCorePlugin != null)
				{
					if ((_cachedCorePlugin == VehicleLicence && VehicleLicence != null && VehicleLicence.IsLoaded) ||
					    (_cachedCorePlugin == RustVehicles && RustVehicles != null && RustVehicles.IsLoaded))
					{
						return _cachedCorePlugin;
					}
					else
					{
						_cachedCorePlugin = null;
						_corePluginCacheValid = false;
					}
				}
				
				var vlLoaded = VehicleLicence != null && VehicleLicence.IsLoaded;
				var rvLoaded = RustVehicles != null && RustVehicles.IsLoaded;
				Plugin result = null;
				
				if (vlLoaded && rvLoaded)
				{
					if (_config.EnableUIDebug)
						DebugUI("[CORE] Both VehicleLicence and RustVehicles loaded. Selecting VehicleLicence as core.");
					result = VehicleLicence;
				}
				else if (vlLoaded)
				{
					if (_config.EnableUIDebug)
						DebugUI("[CORE] Using VehicleLicence as core plugin.");
					result = VehicleLicence;
				}
				else if (rvLoaded)
				{
					if (_config.EnableUIDebug)
						DebugUI("[CORE] Using RustVehicles as core plugin.");
					result = RustVehicles;
				}
				else
				{
					if (_config.EnableUIDebug)
						DebugUI("[CORE] No core vehicle plugin loaded.");
					result = null;
				}
				
				_cachedCorePlugin = result;
				_corePluginCacheValid = true;
				return result;
			}
		}

		private string CorePluginName => CorePlugin == null ? "Vehicle" : (CorePlugin == RustVehicles ? "RustVehicles" : "VehicleLicence");

		private bool HasAnyPermission(BasePlayer player, params string[] permissions)
		{
			if (player == null || permissions == null) return false;
			for (int i = 0; i < permissions.Length; i++)
			{
				if (HasPermission(player, permissions[i])) return true;
			}
			return false;
		}

		private bool HasCoreUsePermission(BasePlayer player)
		{
			return HasAnyPermission(player, "RustVehicles.use", "VehicleLicence.use");
		}

		private IEnumerable<string> GetVehiclePermissionCandidates(string vehicleType)
		{
			var vt = (vehicleType ?? string.Empty).ToLower();
			yield return $"RustVehicles.spawn.{vt}";
			yield return $"RustVehicles.{vt}";
			yield return $"RustVehicles.buy.{vt}";
			yield return $"RustVehicles.purchase.{vt}";
			yield return $"VehicleLicence.spawn.{vt}";
			yield return $"VehicleLicence.{vt}";
			yield return $"VehicleLicence.buy.{vt}";
			yield return $"VehicleLicence.purchase.{vt}";
		}

		private string GetCoreConfigPath()
		{
			var cfgDir = Interface.Oxide.ConfigDirectory;
			var rv = $"{cfgDir}/RustVehicles.json";
			var vl = $"{cfgDir}/VehicleLicence.json";

			if (CorePlugin == VehicleLicence && System.IO.File.Exists(vl))
			{
				DebugUI($"[PATH] Using VehicleLicence config: {vl}");
				return vl;
			}
			if (CorePlugin == RustVehicles && System.IO.File.Exists(rv))
			{
				DebugUI($"[PATH] Using RustVehicles config: {rv}");
				return rv;
			}

			if (System.IO.File.Exists(rv)) { DebugUI($"[PATH] Core not loaded; falling back to RustVehicles config: {rv}"); return rv; }
			if (System.IO.File.Exists(vl)) { DebugUI($"[PATH] Core not loaded; falling back to VehicleLicence config: {vl}"); return vl; }

			var chosen = CorePlugin == VehicleLicence ? vl : rv;
			DebugUI($"[PATH] No config files found; defaulting path to: {chosen}");
			return chosen;
		}

		private Dictionary<string, object> GetVehicleConfig()
		{
			var configPath = GetCoreConfigPath();
			if (!System.IO.File.Exists(configPath))
			{
				PrintWarning($"Vehicle config not found at: {configPath}");
				return null;
			}

			lock (_configCacheLock)
			{
				var fileInfo = new System.IO.FileInfo(configPath);
				var currentCorePlugin = CorePlugin;
				
				var cacheValid = _cachedVehicleConfig != null && 
					fileInfo.LastWriteTime <= _configCacheTimestamp &&
					DateTime.UtcNow - _configCacheTimestamp < CONFIG_CACHE_DURATION;
				
				if (cacheValid)
				{
					DebugUI("[CACHE] Using cached vehicle config");
					return _cachedVehicleConfig;
				}

				DebugUI("[CACHE] Reloading vehicle config cache");
				var jsonContent = System.IO.File.ReadAllText(configPath);
				_cachedVehicleConfig = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
				_configCacheTimestamp = DateTime.UtcNow;
				
				return _cachedVehicleConfig;
			}
		}

		private string GetCoreDataPath()
		{
			var dataDir = Interface.Oxide.DataDirectory;
			var rv = $"{dataDir}/RustVehicles/RustVehicles.json";
			var vl = $"{dataDir}/VehicleLicence/VehicleLicence.json";

			if (CorePlugin == VehicleLicence && System.IO.File.Exists(vl))
			{
				DebugUI($"[PATH] Using VehicleLicence data: {vl}");
				return vl;
			}
			if (CorePlugin == RustVehicles && System.IO.File.Exists(rv))
			{
				DebugUI($"[PATH] Using RustVehicles data: {rv}");
				return rv;
			}

			if (System.IO.File.Exists(rv)) { DebugUI($"[PATH] Core not loaded; falling back to RustVehicles data: {rv}"); return rv; }
			if (System.IO.File.Exists(vl)) { DebugUI($"[PATH] Core not loaded; falling back to VehicleLicence data: {vl}"); return vl; }

			var chosen = CorePlugin == VehicleLicence ? vl : rv;
			DebugUI($"[PATH] No data files found; defaulting path to: {chosen}");
			return chosen;
		}

        private bool HasPermission(BasePlayer player, string perm)
        {
            return permission.UserHasPermission(player.UserIDString, perm);
        }

        private bool HasVehiclePermission(BasePlayer player, string vehicleType)
        {
            if (player == null) return false;

            var vt = (vehicleType ?? string.Empty).ToLower();
            if (vt == "transporthelicopter" || vt == "transportcopter" || vt == "scraptransporthelicopter" || vt == "scrapi")
                vt = "tcop";
            if (vt == "ridablehorse") vt = "horse";
            if (vt == "pedaltrike") vt = "trike";

			if (CorePlugin != null)
            {
				var result = CorePlugin.Call("API_CanUseLicensedVehicle", player, vt);
                if (result is bool b)
                    return b;
            }

			var candidates = GetVehiclePermissionCandidates(vt);

			foreach (var permName in candidates)
            {
                if (HasPermission(player, permName))
                    return true;
            }

            return true;
        }

        private string GetPlayerInfo(BasePlayer player)
        {
            var balance = GetPlayerBalance(player);
            var vehicleCount = GetOwnedVehicleCount(player);
            var maxVehicles = GetMaxVehicles(player);
            
            var vehicleText = maxVehicles <= 0 ? $"{vehicleCount}" : $"{vehicleCount}/{maxVehicles}";
            
            var formattedBalance = balance.Contains("\n") ? balance : balance.Replace(" | ", "\n");
            
            return $"{Lang("Balance", player)}\n{formattedBalance}\n{Lang("VehiclesCount", player, vehicleText)} | {player.displayName}";
        }

        private string GetPlayerBalance(BasePlayer player)
        {
            var currencyInfo = GetUsedCurrenciesFromConfig();
            DebugUI($"[BALANCE] Found {currencyInfo.Count} currencies in config: {string.Join(", ", currencyInfo.Keys)}");
            
            if (currencyInfo.Count == 0)
            {
                var scrapAmount = player.inventory.GetAmount(ItemManager.FindItemDefinition("scrap").itemid);
                DebugUI($"[BALANCE] No currencies found, using fallback scrap: {scrapAmount}");
                return $"{scrapAmount:N0} scrap";
            }

            var balanceParts = new List<string>();
            var scrapFound = false;

            if (currencyInfo.ContainsKey("scrap"))
            {
                var scrapAmount = player.inventory.GetAmount(ItemManager.FindItemDefinition("scrap").itemid);
                balanceParts.Add($"{scrapAmount:N0} scrap");
                scrapFound = true;
            }

            foreach (var kvp in currencyInfo)
            {
                var currency = kvp.Key;
                var displayName = kvp.Value;

                if (string.Equals(currency, "scrap", StringComparison.OrdinalIgnoreCase))
                    continue;

                double balance = 0;

                if (string.Equals(currency, "economics", StringComparison.OrdinalIgnoreCase))
                {
                    if (Economics != null && Economics.IsLoaded)
                    {
                        var ecoBalance = Economics.Call("Balance", player.userID.Get());
                        if (ecoBalance != null && double.TryParse(ecoBalance.ToString(), out balance))
                        {
                        }
                    }
                }
                else if (string.Equals(currency, "serverrewards", StringComparison.OrdinalIgnoreCase))
                {
                    if (ServerRewards != null && ServerRewards.IsLoaded)
                    {
                        var points = ServerRewards.Call("CheckPoints", player.userID.Get());
                        if (points != null && double.TryParse(points.ToString(), out balance))
                        {
                            if (string.IsNullOrEmpty(displayName) || displayName == currency)
                                displayName = "RP";
                        }
                    }
                }
                else
                {
                    var itemDef = ItemManager.FindItemDefinition(currency);
                    if (itemDef != null)
                    {
                        balance = player.inventory.GetAmount(itemDef.itemid);
                        if (string.IsNullOrEmpty(displayName) || displayName == currency)
                            displayName = currency;
                    }
                }

                balanceParts.Add($"{balance:N0} {displayName}");
            }

            if (!scrapFound && balanceParts.Count > 0)
            {
                return string.Join("\n", balanceParts);
            }

            if (balanceParts.Count > 0)
            {
                return string.Join("\n", balanceParts);
            }

            var fallbackScrapAmount = player.inventory.GetAmount(ItemManager.FindItemDefinition("scrap").itemid);
            return $"{fallbackScrapAmount:N0} scrap";
        }

        private Dictionary<string, string> GetUsedCurrenciesFromConfig()
        {
            var currencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                var configPath = GetCoreConfigPath();
                if (!System.IO.File.Exists(configPath))
                {
                    DebugUI($"[CURRENCY SCAN] Config file not found: {configPath}");
                    return currencies;
                }

                var jsonContent = System.IO.File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);

                var sectionsToSearch = new[] { "Modular Vehicle Settings", "Normal Vehicle Settings", "Train Vehicle Settings", "Custom Vehicle Settings" };
                
                foreach (var topKey in sectionsToSearch)
                {
                    if (!config.ContainsKey(topKey) || !(config[topKey] is Newtonsoft.Json.Linq.JObject topObj))
                        continue;

                    foreach (var property in topObj.Properties())
                    {
                        var vehicleData = property.Value as Newtonsoft.Json.Linq.JObject;
                        if (vehicleData == null)
                            continue;

                        var priceSections = new[] { "Purchase Prices", "Spawn Prices", "Recall Prices" };
                        foreach (var priceSection in priceSections)
                        {
                            var prices = vehicleData[priceSection] as Newtonsoft.Json.Linq.JObject;
                            if (prices == null)
                                continue;

                            foreach (var priceProperty in prices.Properties())
                            {
                                var priceData = priceProperty.Value as Newtonsoft.Json.Linq.JObject;
                                if (priceData == null)
                                    continue;

                                var amount = priceData["amount"]?.ToObject<int>() ?? 0;
                                if (amount <= 0)
                                    continue;

                                var priceKey = priceProperty.Name;
                                var displayName = priceData["displayName"]?.ToString();
                                
                                if (!currencies.ContainsKey(priceKey))
                                {
                                    currencies[priceKey] = displayName ?? priceKey;
                                    DebugUI($"[CURRENCY SCAN] Found currency '{priceKey}' with amount {amount} and displayName '{displayName}' in {property.Name} {priceSection}");
                                }
                                else if (!string.IsNullOrEmpty(displayName) && (priceSection == "Purchase Prices" || string.IsNullOrEmpty(currencies[priceKey]) || currencies[priceKey] == priceKey))
                                {
                                    currencies[priceKey] = displayName;
                                }
                            }
                        }
                    }
                }
                
                DebugUI($"[CURRENCY SCAN] Total unique currencies found: {currencies.Count} - {string.Join(", ", currencies)}");
            }
            catch (Exception ex)
            {
                DebugUI($"[CURRENCY SCAN] Error scanning currencies: {ex.Message}");
                PrintWarning($"[CURRENCY SCAN] Error: {ex.StackTrace}");
            }

            return currencies;
        }

        private int GetOwnedVehicleCount(BasePlayer player)
        {
            try
            {
                var ownedVehicles = GetOwnedVehicles(player);
                DebugUI($"[VEHICLE COUNT DEBUG] Player {player.userID} owns {ownedVehicles?.Count ?? 0} vehicles");
                return ownedVehicles?.Count ?? 0;
            }
            catch (System.Exception ex)
            {
                PrintWarning($"[VEHICLE COUNT ERROR] {ex.Message}");
                return 0;
            }
        }

        private int GetMaxVehicles(BasePlayer player)
        {
            try
            {
                var config = GetVehicleConfig();
                if (config?.ContainsKey("Global Settings") == true &&
                    config["Global Settings"] is Newtonsoft.Json.Linq.JObject globalSettings)
                {
                    var limitToken = globalSettings["Limit Vehicles"];
                    if (limitToken != null && int.TryParse(limitToken.ToString(), out int limit))
                    {
                        return limit;
                    }
                }
            }
            catch (System.Exception ex)
            {
                PrintWarning($"[MAX VEHICLES ERROR] {ex.Message}");
            }
                return 0;

        }
        
        private List<VehicleDisplayInfo> GetAvailableVehicles(BasePlayer player, string category = "all")
        {
            if (_config.EnableTestMode)
            {
                DebugUI("[TEST MODE] Using sample vehicle data for shop");
                return GetSampleVehicleData(player, category);
            }

            var vehicles = GetVehiclesFromConfig(player, category);
            
            vehicles.RemoveAll(v => 
                v.DisplayName.Equals("Tomaha Snowmobile", StringComparison.OrdinalIgnoreCase) ||
                v.VehicleType.Equals("tomahasnowmobile", StringComparison.OrdinalIgnoreCase) ||
                v.ImageKey.Equals("tomahasnowmobile", StringComparison.OrdinalIgnoreCase) ||
                v.DisplayName.Contains("Tomaha", StringComparison.OrdinalIgnoreCase));

            return vehicles;
        }

        private List<VehicleDisplayInfo> GetVehiclesFromConfig(BasePlayer player, string category = "all")
        {
            var vehicles = new List<VehicleDisplayInfo>();
            
            HashSet<string> ownedVehiclesSet = null;
            if (player != null)
            {
                var ownedVehicleNames = ReadVehicleDataDirectly(player.userID);
                if (ownedVehicleNames != null && ownedVehicleNames.Count > 0)
                {
                    ownedVehiclesSet = new HashSet<string>(ownedVehicleNames, StringComparer.OrdinalIgnoreCase);
                    DebugUI($"[VEHICLE OWNERSHIP] Cached {ownedVehiclesSet.Count} owned vehicles for player {player.userID}");
                }
            }
            
            try
            {
                var config = GetVehicleConfig();
                if (config == null)
                {
                    PrintWarning($"Vehicle config not found or invalid");
                    return vehicles;
                }

                var sectionsToSearch = new[] { "Modular Vehicle Settings", "Normal Vehicle Settings", "Train Vehicle Settings", "Custom Vehicle Settings" };
                
                foreach (var sectionKey in sectionsToSearch)
                {
                    if (!config.ContainsKey(sectionKey) || !(config[sectionKey] is Newtonsoft.Json.Linq.JObject sectionSettings))
                        continue;

                    foreach (var vehicleProperty in sectionSettings.Properties())
                    {
                        var vehicleData = vehicleProperty.Value as Newtonsoft.Json.Linq.JObject;
                        if (vehicleData == null)
                            continue;

                        var vehicle = CreateVehicleFromConfigData(vehicleProperty.Name, vehicleData, player, ownedVehiclesSet);
                        if (vehicle != null && (category == "all" || vehicle.Category == category))
                            vehicles.Add(vehicle);
                    }
                }

                DebugUI($"[CONFIG] Loaded {vehicles.Count} vehicles for category '{category}'");
            }
            catch (Exception ex)
            {
                PrintWarning($"Error reading vehicles from RustVehicles.json: {ex.Message}");
            }

            return vehicles;
        }

        private VehicleDisplayInfo CreateVehicleFromConfig(string vehicleType, Dictionary<string, object> vData, BasePlayer player)
        {
            try
            {
                if (!vData.ContainsKey("Purchasable") || !(bool)vData["Purchasable"])
                    return null;

                var displayName = vData.ContainsKey("Display Name") ? vData["Display Name"].ToString() : FormatVehicleName(vehicleType);
                
                bool isOwned = false;
                if (player != null)
                {
                    var ownedVehicleNames = ReadVehicleDataDirectly(player.userID);
                    if (ownedVehicleNames != null)
                    {
                        foreach (var owned in ownedVehicleNames)
                        {
                            if (string.Equals(owned, vehicleType, StringComparison.OrdinalIgnoreCase))
                            {
                                isOwned = true;
                                break;
                            }
                        }
                        if (isOwned)
                        {
                            DebugUI($"[VEHICLE OWNERSHIP] Player {player.userID} already owns {vehicleType}");
                        }
                        else
                        {
                            DebugUI($"[VEHICLE OWNERSHIP] Player {player.userID} does NOT own {vehicleType} (checked against {ownedVehicleNames.Count} owned vehicles: {string.Join(", ", ownedVehicleNames)})");
                        }
                    }
                }
                
                var priceInfo = GetConfigPriceInfo(vData, player);
                var canAfford = CanPlayerAfford(player, vData);
                
                if (isOwned)
                {
                    canAfford = false;
                    priceInfo = Lang("AlreadyOwned", player);
                    DebugUI($"[VEHICLE OWNERSHIP] Setting {vehicleType} to unaffordable (already owned)");
                }
                else if (player != null && !HasVehiclePermission(player, vehicleType))
                {
                    canAfford = false;
                }
                
                var vehicleCategory = DetermineVehicleCategory(vehicleType, displayName);

                return new VehicleDisplayInfo
                {
                    VehicleType = vehicleType,
                    DisplayName = displayName,
                    Image = null /* deferred */,
                    StatusInfo = priceInfo,
                    CanAfford = canAfford,
                    IsSpawned = false,
                    Category = vehicleCategory
                };
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to create vehicle {vehicleType}: {ex.Message}");
                return null;
            }
        }

        private VehicleDisplayInfo CreateVehicleFromConfigData(string vehicleType, Newtonsoft.Json.Linq.JObject vehicleData, BasePlayer player, HashSet<string> ownedVehiclesSet = null)
        {
            try
            {
                var purchasableToken = vehicleData["Purchasable"];
                bool isPurchasable = false;
                if (purchasableToken != null)
                {
                    try
                    {
                        isPurchasable = purchasableToken.ToObject<bool>();
                    }
                    catch
                    {
                        isPurchasable = false;
                    }
                }
                
                if (!isPurchasable)
                {
                    if (_config.EnableUIDebug)
                        DebugUI($"[PURCHASABLE] Skipping {vehicleType} - Purchasable is false or missing");
                    return null;
                }

				var displayName = vehicleData["Display Name"]?.ToString() ?? FormatVehicleName(vehicleType);
                
                var actualVehicleType = vehicleType.ToLower();
                var commandsArray = vehicleData["Commands"] as Newtonsoft.Json.Linq.JArray;
                if (commandsArray != null && commandsArray.Count > 0)
                {
                    actualVehicleType = commandsArray[0].ToString().ToLower();
                }
                
                bool isOwned = false;
                if (player != null && ownedVehiclesSet != null)
                {
                    var internalVehicleType = MapConfigKeyToVehicleType(vehicleType);
                    var normalizedVehicleType = NormalizeVehicleName(vehicleType);
                    var normalizedInternalType = NormalizeVehicleName(internalVehicleType);
                    
                    isOwned = ownedVehiclesSet.Contains(vehicleType) 
                           || ownedVehiclesSet.Contains(internalVehicleType)
                           || ownedVehiclesSet.Contains(normalizedVehicleType)
                           || ownedVehiclesSet.Contains(normalizedInternalType);
                    
                    if (!isOwned)
                    {
                        foreach (var owned in ownedVehiclesSet)
                        {
                            var normalizedOwned = NormalizeVehicleName(owned);
                            if (string.Equals(normalizedVehicleType, normalizedOwned, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(normalizedInternalType, normalizedOwned, StringComparison.OrdinalIgnoreCase))
                            {
                                isOwned = true;
                                if (_config.EnableUIDebug)
                                    DebugUI($"[VEHICLE OWNERSHIP] Matched {vehicleType} (internal: {internalVehicleType}) with owned vehicle {owned} via normalization");
                                break;
                            }
                        }
                    }
                    
                    if (isOwned && _config.EnableUIDebug)
                    {
                        DebugUI($"[VEHICLE OWNERSHIP] Player {player.userID} owns {vehicleType} (internal: {internalVehicleType})");
                    }
                }
                
                var priceInfo = GetConfigPriceInfoFromJObject(vehicleData, player);
                var canAfford = CanPlayerAffordFromJObject(player, vehicleData);
                
                if (isOwned)
                {
                    canAfford = false;
                    priceInfo = Lang("AlreadyOwned", player);
                    DebugUI($"[VEHICLE OWNERSHIP] Setting {vehicleType} to unaffordable (already owned)");
                }
                else if (player != null && !HasVehiclePermission(player, actualVehicleType))
                {
                    canAfford = false;
                }
                
                var vehicleCategory = DetermineVehicleCategory(actualVehicleType, displayName);

				return new VehicleDisplayInfo
                {
					VehicleType = actualVehicleType,
					DisplayName = displayName,
                    Image = null,
                    StatusInfo = priceInfo,
                    CanAfford = canAfford,
                    IsSpawned = false,
					Category = vehicleCategory,
					ImageKey = GetImageKeyFromSection(vehicleType)
                };
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to create vehicle {vehicleType}: {ex.Message}");
                return null;
            }
        }

        private string ExtractVehicleType(string configKey)
        {
            var vehicleType = configKey.Replace(" Vehicle", "").Replace(" ", "").ToLower();
            
            var result = vehicleType switch
            {
                "pedaltrike" => "trike",      
                "ridablehorse" => "horse",    
                "transporthelicopter" => "tcop", 
                "transportcopter" => "tcop", 
                "scraptransporthelicopter" => "tcop", 
                "scrapi" => "tcop", 
                _ => vehicleType
            };
            
            return result;
        }

        private string DetermineVehicleCategory(string vehicleType, string displayName)
        {
            var type = vehicleType.ToLower();
            
            if (_karuzaVehicleCategories.TryGetValue(type, out var karuzaCategory))
            {
                DebugKaruzaVehicles($"[CATEGORY] Using Karuza category '{karuzaCategory}' for vehicle '{type}'");
                return karuzaCategory;
            }
            
            var name = displayName.ToLower();
            
            if (name.Contains("shopping cart") || type.Contains("shoppingcart") ||
                name.Contains("spooky shopping cart") || type.Contains("spookyshoppingcart") ||
                name.Contains("luggage cart") || type.Contains("luggagecart") ||
                name.Contains("train wreck") || type.Contains("trainwreck") ||
                name.Contains("train wrecker") || type.Contains("trainwrecker"))
                return "land";

            if (type.Contains("heli") || type.Contains("copter") || type.Contains("chinook") || 
                type.Contains("balloon") || type.Contains("fighter") || type.Contains("plane") || 
                type.Contains("wing") || type.Contains("ufo") || type.Contains("glider") || 
                type.Contains("raptor") || type.Contains("drone") || type.Contains("air") ||
                type.Contains("littlebird") || type.Contains("warbird") || type.Contains("ah69") ||
                type.Contains("mavik") || type.Contains("waterheli") || type.Contains("patrolheli") ||
                type.Contains("cobra") || type.Contains("tardis") || type.Contains("talon") ||
                type.Contains("falcon") || type.Contains("mamba") || type.Contains("orlik") ||
                type.Contains("skyboat") || type.Contains("skyplane") || type.Contains("skywing") ||
                type.Contains("starfighter") || type.Contains("oppressor") || type.Contains("shuttle") ||
                type.Contains("speeder") || type.Contains("f15") || type.Contains("a10") ||
                type.Contains("batwing") || type.Contains("flyingboat") || type.Contains("invader") ||
                type.Contains("santa") || type.Contains("witch") || type.Contains("carpet") ||
                type.Contains("predator") ||
                name.Contains("helicopter") || name.Contains("balloon") || name.Contains("copter") ||
                name.Contains("fighter") || name.Contains("plane") || name.Contains("wing") ||
                name.Contains("ufo") || name.Contains("glider") || name.Contains("air") ||
                name.Contains("predator"))
                return "air";
                
            if (type.Contains("boat") || type.Contains("rhib") || type.Contains("kayak") || 
                type.Contains("submarine") || type.Contains("dpv") || type.Contains("tugboat") ||
                type.Contains("rowboat") || name.Contains("boat") || name.Contains("submarine") || 
                name.Contains("rhib") || name.Contains("water") || name.Contains("tug"))
                return "water";
                
            if ((type.Contains("train") || type.Contains("cart") || type.Contains("locomotive") ||
                type.Contains("rail") || type.Contains("workcart") || name.Contains("train") || 
                name.Contains("cart") || name.Contains("locomotive") || name.Contains("rail")) &&
                !type.Contains("trailer") && !name.Contains("trailer") &&
                !type.Contains("shoppingcart") && !name.Contains("shopping cart") &&
                !type.Contains("luggagecart") && !name.Contains("luggage cart") &&
                !type.Contains("trainwreck") && !name.Contains("train wreck") &&
                !type.Contains("trainwrecker") && !name.Contains("train wrecker"))
                return "train";
                
            if (type.Contains("siege") || type.Contains("catapult") || type.Contains("ballista") || 
                type.Contains("batteringram") || type.Contains("battering") || type.Contains("ram") ||
                name.Contains("siege") || name.Contains("catapult") || name.Contains("ballista") || 
                name.Contains("battering"))
                return "siege";
                
            return "land";
        }

        private string GetConfigPriceInfo(Dictionary<string, object> vData, BasePlayer player = null)
        {
            try
            {
                if (vData.ContainsKey("Spawn Prices") && vData["Spawn Prices"] is Dictionary<string, object> spawnPrices)
                {
                    foreach (var priceKvp in spawnPrices)
                    {
                        if (priceKvp.Value is Dictionary<string, object> priceData &&
                            priceData.ContainsKey("amount") && priceData.ContainsKey("displayName"))
                        {
                            var amount = priceData["amount"];
                            var displayName = priceData["displayName"].ToString();
                            
                            if (amount.ToString() == "0")
                                return Lang("Free", player);
                                
                            return $"{amount} {displayName}";
                        }
                    }
                }
                
                return Lang("Free", player);
            }
            catch
            {
                return Lang("UnknownPrice", player);
            }
        }

        private bool CanPlayerAfford(BasePlayer player, Dictionary<string, object> vData)
        {
            try
            {
                if (vData.ContainsKey("Spawn Prices") && vData["Spawn Prices"] is Dictionary<string, object> spawnPrices)
                {
                    foreach (var priceKvp in spawnPrices)
                    {
                        var priceKey = priceKvp.Key.ToLower();
                        if (priceKvp.Value is Dictionary<string, object> priceData &&
                            priceData.ContainsKey("amount"))
                        {
                            var amount = Convert.ToInt32(priceData["amount"]);
                            double playerBalance = 0;
                            
                            if (priceKey == "economics")
                            {
                                if (Economics != null && Economics.IsLoaded)
                                {
                                    var balance = Economics.Call("Balance", player.userID.Get());
                                    if (balance != null && double.TryParse(balance.ToString(), out var ecoBalance))
                                    {
                                        playerBalance = ecoBalance;
                                        DebugUI($"[PRICE DEBUG] Player has {playerBalance} Economics");
                                    }
                                    else
                                    {
                                        return false;
                                    }
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else if (priceKey == "serverrewards")
                            {
                                if (ServerRewards != null && ServerRewards.IsLoaded)
                                {
                                    var points = ServerRewards.Call("CheckPoints", player.userID.Get());
                                    if (points != null && double.TryParse(points.ToString(), out var srBalance))
                                    {
                                        playerBalance = srBalance;
                                        DebugUI($"[PRICE DEBUG] Player has {playerBalance} ServerRewards points");
                                    }
                                    else
                                    {
                                        return false;
                                    }
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                var itemDefinition = ItemManager.FindItemDefinition(priceKey);
                                if (itemDefinition != null)
                                {
                                    playerBalance = player.inventory.GetAmount(itemDefinition.itemid);
                                    DebugUI($"[PRICE DEBUG] Player has {playerBalance} {priceKey}");
                                }
                                else
                                {
                                    DebugUI($"[PRICE DEBUG] Unknown currency type: {priceKey}");
                                    return false;
                                }
                            }
                            
                            DebugUI($"[PRICE DEBUG] Vehicle costs {amount} {priceKey}");
                            return amount <= playerBalance;
                        }
                    }
                }
                
                return true; 
            }
            catch (Exception ex)
            {
                PrintWarning($"[PRICE ERROR] {ex.Message}");
                return true; 
            }
        }

        private string GetConfigPriceInfoFromJObject(Newtonsoft.Json.Linq.JObject vehicleData, BasePlayer player = null)
        {
            try
            {
                var purchasePrices = vehicleData["Purchase Prices"] as Newtonsoft.Json.Linq.JObject;
                if (purchasePrices != null)
                {
                    foreach (var priceProperty in purchasePrices.Properties())
                    {
                        var priceData = priceProperty.Value as Newtonsoft.Json.Linq.JObject;
                        if (priceData != null)
                        {
                            var amount = priceData["amount"]?.ToObject<int>() ?? 0;
                            var displayName = priceData["displayName"]?.ToString() ?? Lang("Unknown", player);

                            if (amount == 0)
                                return Lang("Free", player);

                            return $"{amount} {displayName}";
                        }
                    }
                }

                return Lang("Free", player);
            }
            catch
            {
                return Lang("PriceUnknown", player);
            }
        }

        private bool CanPlayerAffordFromJObject(BasePlayer player, Newtonsoft.Json.Linq.JObject vehicleData)
        {
            try
            {
                var purchasePrices = vehicleData["Purchase Prices"] as Newtonsoft.Json.Linq.JObject;
                if (purchasePrices != null)
                {
                    foreach (var priceProperty in purchasePrices.Properties())
                    {
                        var priceKey = priceProperty.Name.ToLower();
                        var priceData = priceProperty.Value as Newtonsoft.Json.Linq.JObject;
                        if (priceData != null)
                        {
                            var amount = priceData["amount"]?.ToObject<int>() ?? 0;
                            var displayName = vehicleData["Display Name"]?.ToString() ?? "Unknown Vehicle";
                            
                            double playerBalance = 0;
                            
                            if (priceKey == "economics")
                            {
                                if (Economics != null && Economics.IsLoaded)
                                {
                                    var balance = Economics.Call("Balance", player.userID.Get());
                                    if (balance != null && double.TryParse(balance.ToString(), out var ecoBalance))
                                    {
                                        playerBalance = ecoBalance;
                                        if (_config.EnableUIDebug)
                                            DebugUI($"[VEHICLE PRICE] Player has {playerBalance} Economics");
                                    }
                                    else
                                    {
                                        if (_config.EnableUIDebug)
                                            DebugUI($"[VEHICLE PRICE] Could not get Economics balance");
                                        return false;
                                    }
                                }
                                else
                                {
                                    if (_config.EnableUIDebug)
                                        DebugUI($"[VEHICLE PRICE] Economics plugin not loaded");
                                    return false;
                                }
                            }
                            else if (priceKey == "serverrewards")
                            {
                                if (ServerRewards != null && ServerRewards.IsLoaded)
                                {
                                    var points = ServerRewards.Call("CheckPoints", player.userID.Get());
                                    if (points != null && double.TryParse(points.ToString(), out var srBalance))
                                    {
                                        playerBalance = srBalance;
                                        if (_config.EnableUIDebug)
                                            DebugUI($"[VEHICLE PRICE] Player has {playerBalance} ServerRewards points");
                                    }
                                    else
                                    {
                                        if (_config.EnableUIDebug)
                                            DebugUI($"[VEHICLE PRICE] Could not get ServerRewards points");
                                        return false;
                                    }
                                }
                                else
                                {
                                    if (_config.EnableUIDebug)
                                        DebugUI($"[VEHICLE PRICE] ServerRewards plugin not loaded");
                                    return false;
                                }
                            }
                            else
                            {
                                var itemDefinition = ItemManager.FindItemDefinition(priceKey);
                                if (itemDefinition != null)
                                {
                                    playerBalance = player.inventory.GetAmount(itemDefinition.itemid);
                                    if (_config.EnableUIDebug)
                                        DebugUI($"[VEHICLE PRICE] Player has {playerBalance} {priceKey}");
                                }
                                else
                                {
                                    if (_config.EnableUIDebug)
                                        DebugUI($"[VEHICLE PRICE] Unknown currency type: {priceKey}");
                                    return false;
                                }
                            }
                            
                            var canAfford = playerBalance >= amount;
                            if (_config.EnableUIDebug)
                                DebugUI($"[VEHICLE PRICE] {displayName} costs {amount} {priceKey}, player can afford: {canAfford}");
                            
                            return canAfford;
                        }
                    }
                }
                
                return true; 
            }
            catch (Exception ex)
            {
                PrintWarning($"[PRICE ERROR] {ex.Message}");
                return true; 
            }
        }

        private string GetVehiclePriceFromEconomy(BasePlayer player, string vehicleType)
        {
            try
            {
				var configPath = GetCoreConfigPath();
                if (System.IO.File.Exists(configPath))
                {
                    var jsonContent = System.IO.File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                    
                    var sections = new[] { "Normal Vehicle Settings", "Custom Vehicle Settings" };
                    
                    foreach (var section in sections)
                    {
                        if (config.ContainsKey(section) && config[section] is Newtonsoft.Json.Linq.JObject settings)
                        {
                            foreach (var prop in settings.Properties())
                            {
                                var commands = prop.Value["Commands"] as Newtonsoft.Json.Linq.JArray;
                                bool found = false;
                                if (commands != null)
                                {
                                    foreach (var cmd in commands)
                                    {
                                        if (cmd.ToString().ToLower() == vehicleType.ToLower())
                                        {
                                            found = true;
                                            break;
                                        }
                                    }
                                }
                                if (found)
                                {
                                    var purchasePrices = prop.Value["Purchase Prices"] as Newtonsoft.Json.Linq.JObject;
                                    if (purchasePrices != null)
                                    {
                                        foreach (var priceProperty in purchasePrices.Properties())
                                        {
                                            var priceData = priceProperty.Value as Newtonsoft.Json.Linq.JObject;
                                            if (priceData != null)
                                            {
                                                var amount = priceData["amount"]?.ToObject<int>() ?? 0;
                                                var displayName = priceData["displayName"]?.ToString() ?? Lang("Unknown", player);
                                                
                                                if (amount == 0)
                                                    return Lang("Free", player);
                                                
                                                return $"{amount:N0} {displayName}";
                                            }
                                        }
                                    }
                                    return Lang("Free", player);
                                }
                            }
                        }
                    }
                }
                
                return "5000 Scrap";
            }
            catch
            {
                return Lang("PriceUnknown", player);
            }
        }

        private bool CanPlayerAffordVehicle(BasePlayer player, string vehicleType)
        {
            try
            {
				var economyType = GetConfiguredEconomyType();
                
                var playerBalance = GetPlayerBalanceForEconomy(player, economyType);
                
                var vehiclePrice = GetVehiclePriceAmount(vehicleType);
                
                return playerBalance >= vehiclePrice;
            }
            catch (Exception ex)
            {
                PrintWarning($"[AFFORDABILITY ERROR] {ex.Message}");
                return true;
            }
        }

        private string GetConfiguredEconomyType()
        {
            try
            {
				var configPath = GetCoreConfigPath();
                if (System.IO.File.Exists(configPath))
                {
                    var jsonContent = System.IO.File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                    
                    if (config.ContainsKey("Global Settings") && config["Global Settings"] is Newtonsoft.Json.Linq.JObject globalSettings)
                    {
                        var economyType = globalSettings["Economy Type"]?.ToString();
                        return economyType ?? "Scrap";
                    }
                }
                return "Scrap";
            }
            catch
            {
                return "Scrap";
            }
        }

        private double GetPlayerBalanceForEconomy(BasePlayer player, string economyType)
        {
            switch (economyType?.ToLower())
            {
                case "economics":
                    if (Economics != null && Economics.IsLoaded)
                    {
                        var balance = Economics.Call("Balance", player.userID.Get());
                        if (balance != null && double.TryParse(balance.ToString(), out var ecoBalance))
                            return ecoBalance;
                    }
                    break;
                    
                case "serverrewards":
                    if (ServerRewards != null && ServerRewards.IsLoaded)
                    {
                        var points = ServerRewards.Call("CheckPoints", player.userID.Get());
                        if (points != null && double.TryParse(points.ToString(), out var srBalance))
                            return srBalance;
                    }
                    break;
                    
                case "scrap":
                default:
                    return player.inventory.GetAmount(ItemManager.FindItemDefinition("scrap").itemid);
            }
            
            return player.inventory.GetAmount(ItemManager.FindItemDefinition("scrap").itemid);
        }

        private int GetVehiclePriceAmount(string vehicleType)
        {
            try
            {
				var configPath = GetCoreConfigPath();
                if (System.IO.File.Exists(configPath))
                {
                    var jsonContent = System.IO.File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                    
                    var sections = new[] { "Normal Vehicle Settings", "Custom Vehicle Settings" };
                    
                    foreach (var section in sections)
                    {
                        if (config.ContainsKey(section) && config[section] is Newtonsoft.Json.Linq.JObject settings)
                        {
                            foreach (var prop in settings.Properties())
                            {
                                var commands = prop.Value["Commands"] as Newtonsoft.Json.Linq.JArray;
                                bool found = false;
                                if (commands != null)
                                {
                                    foreach (var cmd in commands)
                                    {
                                        if (cmd.ToString().ToLower() == vehicleType.ToLower())
                                        {
                                            found = true;
                                            break;
                                        }
                                    }
                                }
                                if (found)
                                {
                                    var purchasePrices = prop.Value["Purchase Prices"] as Newtonsoft.Json.Linq.JObject;
                                    if (purchasePrices != null)
                                    {
                                        foreach (var priceProperty in purchasePrices.Properties())
                                        {
                                            var priceData = priceProperty.Value as Newtonsoft.Json.Linq.JObject;
                                            if (priceData != null)
                                            {
                                                return priceData["amount"]?.ToObject<int>() ?? 0;
                                            }
                                        }
                                    }
                                    return 0;
                                }
                            }
                        }
                    }
                }
                
                return 10000;
            }
            catch
            {
                return 10000;
            }
        }

        private List<VehicleDisplayInfo> GetSampleVehicleData(BasePlayer player, string category = "all")
        {
            var allSampleVehicles = new List<VehicleDisplayInfo>();
            
            var vanillaVehicles = new List<VehicleDisplayInfo>
            {
                new VehicleDisplayInfo { VehicleType = "mini", DisplayName = "Mini Copter", StatusInfo = "750 Scrap", CanAfford = true, IsSpawned = false, Category = "air" },
                new VehicleDisplayInfo { VehicleType = "attack", DisplayName = "Attack Helicopter", StatusInfo = "2250 Scrap", CanAfford = true, IsSpawned = false, Category = "air" },
                new VehicleDisplayInfo { VehicleType = "tcop", DisplayName = "Transport Copter", StatusInfo = "1250 Scrap", CanAfford = true, IsSpawned = false, Category = "air" },
                new VehicleDisplayInfo { VehicleType = "chinook", DisplayName = "Chinook", StatusInfo = "Free", CanAfford = true, IsSpawned = false, Category = "air" },
                new VehicleDisplayInfo { VehicleType = "hab", DisplayName = "Hot Air Balloon", StatusInfo = "500 Scrap", CanAfford = true, IsSpawned = false, Category = "air" },
                new VehicleDisplayInfo { VehicleType = "ahab", DisplayName = "Armored Hot Air Balloon", StatusInfo = "500 Scrap", CanAfford = true, IsSpawned = false, Category = "air" },
                
                new VehicleDisplayInfo { VehicleType = "sedan", DisplayName = "Sedan", StatusInfo = "300 Scrap", CanAfford = true, IsSpawned = false, Category = "land" },
                new VehicleDisplayInfo { VehicleType = "horse", DisplayName = "Ridable Horse", StatusInfo = "Free", CanAfford = true, IsSpawned = false, Category = "land" },
                new VehicleDisplayInfo { VehicleType = "pedalbike", DisplayName = "Bicycle", StatusInfo = "Free", CanAfford = true, IsSpawned = false, Category = "land" },
                new VehicleDisplayInfo { VehicleType = "pedaltrike", DisplayName = "Trike", StatusInfo = "Free", CanAfford = true, IsSpawned = false, Category = "land" },
                new VehicleDisplayInfo { VehicleType = "motorbike", DisplayName = "Motorbike", StatusInfo = "Free", CanAfford = true, IsSpawned = false, Category = "land" },
                new VehicleDisplayInfo { VehicleType = "motorbikeseidecar", DisplayName = "Motorbike With Sidecar", StatusInfo = "Free", CanAfford = true, IsSpawned = false, Category = "land" },
                new VehicleDisplayInfo { VehicleType = "snowmobile", DisplayName = "Snowmobile", StatusInfo = "500 Scrap", CanAfford = true, IsSpawned = false, Category = "land" },
                new VehicleDisplayInfo { VehicleType = "magnetcrane", DisplayName = "Magnet Crane", StatusInfo = "2000 Scrap", CanAfford = false, IsSpawned = false, Category = "land" },
                
                new VehicleDisplayInfo { VehicleType = "tugboat", DisplayName = "Tugboat", StatusInfo = "5000 Scrap", CanAfford = true, IsSpawned = false, Category = "water" },
                new VehicleDisplayInfo { VehicleType = "rowboat", DisplayName = "Row Boat", StatusInfo = "25 Scrap", CanAfford = true, IsSpawned = false, Category = "water" },
                new VehicleDisplayInfo { VehicleType = "rhib", DisplayName = "RHIB", StatusInfo = "1000 Scrap", CanAfford = true, IsSpawned = false, Category = "water" },
                new VehicleDisplayInfo { VehicleType = "kayak", DisplayName = "Kayak", StatusInfo = "Free", CanAfford = true, IsSpawned = false, Category = "water" },
                new VehicleDisplayInfo { VehicleType = "subsolo", DisplayName = "Solo Submarine", StatusInfo = "1500 Scrap", CanAfford = true, IsSpawned = false, Category = "water" },
                new VehicleDisplayInfo { VehicleType = "subduo", DisplayName = "Duo Submarine", StatusInfo = "2000 Scrap", CanAfford = true, IsSpawned = false, Category = "water" },
                new VehicleDisplayInfo { VehicleType = "dpv", DisplayName = "Diver Propulsion Vehicle", StatusInfo = "Free", CanAfford = true, IsSpawned = false, Category = "water" },
                
                new VehicleDisplayInfo { VehicleType = "workcart", DisplayName = "Work Cart", StatusInfo = "750 Scrap", CanAfford = true, IsSpawned = false, Category = "train" },
                new VehicleDisplayInfo { VehicleType = "sedanrail", DisplayName = "Sedan Rail", StatusInfo = "300 Scrap", CanAfford = true, IsSpawned = false, Category = "train" },
                new VehicleDisplayInfo { VehicleType = "workcartaboveground", DisplayName = "Work Cart Above Ground", StatusInfo = "2000 Scrap", CanAfford = true, IsSpawned = false, Category = "train" },
                new VehicleDisplayInfo { VehicleType = "workcartcovered", DisplayName = "Covered Work Cart", StatusInfo = "2000 Scrap", CanAfford = true, IsSpawned = false, Category = "train" },
                new VehicleDisplayInfo { VehicleType = "locomotive", DisplayName = "Locomotive", StatusInfo = "2000 Scrap", CanAfford = true, IsSpawned = false, Category = "train" },
                
                new VehicleDisplayInfo { VehicleType = "siegetower", DisplayName = "Siege Tower", StatusInfo = "1000 Scrap", CanAfford = true, IsSpawned = false, Category = "siege" },
                new VehicleDisplayInfo { VehicleType = "catapult", DisplayName = "Catapult", StatusInfo = "800 Scrap", CanAfford = true, IsSpawned = false, Category = "siege" },
                new VehicleDisplayInfo { VehicleType = "batteringram", DisplayName = "Battering Ram", StatusInfo = "600 Scrap", CanAfford = true, IsSpawned = false, Category = "siege" },
                new VehicleDisplayInfo { VehicleType = "ballista", DisplayName = "Mounted Ballista", StatusInfo = "700 Scrap", CanAfford = true, IsSpawned = false, Category = "siege" }
            };

            allSampleVehicles.AddRange(vanillaVehicles);
            
            var allCustomVehicles = LoadCustomVehiclesFromKaruzaManager();

            foreach (var vehicleType in allCustomVehicles)
            {
                var displayName = FormatVehicleName(vehicleType);
                var vehicleCategory = DetermineVehicleCategory(vehicleType, displayName);
                
                allSampleVehicles.Add(new VehicleDisplayInfo 
                { 
                    VehicleType = vehicleType, 
                    DisplayName = displayName, 
                    StatusInfo = "5000 Scrap", 
                    CanAfford = true, 
                    IsSpawned = false, 
                    Category = vehicleCategory 
                });
            }

            var filteredVehicles = new List<VehicleDisplayInfo>();
            
            var scrapAmount = player?.inventory != null ? 
                player.inventory.GetAmount(ItemManager.FindItemDefinition("scrap").itemid) : 
                10000;
            PrintWarning($"[VEHICLE PRICE] Player has {scrapAmount} scrap");
            
            foreach (var vehicle in allSampleVehicles)
            {
				vehicle.Image = null;
				vehicle.ImageKey = GetImageKeyForCommand(vehicle.VehicleType);
                
                if (vehicle.StatusInfo.Contains("Free"))
                {
                    vehicle.CanAfford = true;
                    vehicle.StatusInfo = Lang("Free", player);
                }
                else if (vehicle.StatusInfo.Contains("Scrap"))
                {
                    string priceText = vehicle.StatusInfo.Replace(" Scrap", "").Trim();
                    if (int.TryParse(priceText, out int price))
                    {
                        vehicle.CanAfford = price <= scrapAmount;

                    }
                    else
                    {
                        vehicle.CanAfford = true;
                    }
                }
                else
                {
                    vehicle.CanAfford = true;
                }
                
                if (player != null && !HasVehiclePermission(player, vehicle.VehicleType))
                {
                    vehicle.CanAfford = false;
                }
                
                if (category == "all" || vehicle.Category == category)
                {
                    filteredVehicles.Add(vehicle);
                }
            }

            DebugUI($"[TEST MODE] Sample vehicle data returning {filteredVehicles.Count} vehicles for category '{category}'");
            return filteredVehicles;
        }

        private List<string> LoadCustomVehiclesFromKaruzaManager()
        {
            var customVehicles = new List<string>();
            
            try
            {
                var configPath = $"{Interface.Oxide.ConfigDirectory}/KaruzaCatalog/KaruzaVehicleItemManager.cs";
                if (!System.IO.File.Exists(configPath))
                {
                    PrintWarning($"KaruzaVehicleItemManager.cs not found at: {configPath}");
                    return customVehicles;
                }
                
                var fileContent = System.IO.File.ReadAllText(configPath);
                
                var lines = fileContent.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("PrefabPath = \"assets/custom/") && line.Contains(".prefab\""))
                    {
                        var start = line.IndexOf("\"assets/custom/") + "\"assets/custom/".Length;
                        var end = line.IndexOf(".prefab\"", start);
                        
                        if (end > start)
                        {
                            var vehicleType = line.Substring(start, end - start).ToLower();
                            customVehicles.Add(vehicleType);
                        }
                    }
                }
                
                DebugKaruzaVehicles($"[KARUZA LOADER] Loaded {customVehicles.Count} custom vehicles from KaruzaVehicleItemManager.cs");
                var first10 = new List<string>();
                int count = 0;
                foreach (var vehicle in customVehicles)
                {
                    if (count >= 10) break;
                    first10.Add(vehicle);
                    count++;
                }
                DebugKaruzaVehicles($"[KARUZA LOADER] First 10 vehicles: {string.Join(", ", first10)}");
            }
            catch (Exception ex)
            {
                PrintWarning($"Error loading custom vehicles from KaruzaVehicleItemManager.cs: {ex.Message}");
            }
            
            return customVehicles;
        }

        private void LoadKaruzaVehicleCategories()
        {
            _karuzaVehicleCategories.Clear();
            
            try
            {
                var configPath = $"{Interface.Oxide.ConfigDirectory}/KaruzaCatalog/KaruzaVehicleItemManager.cs";
                if (!System.IO.File.Exists(configPath))
                {
                    DebugKaruzaVehicles($"KaruzaVehicleItemManager.cs not found at: {configPath}");
                    return;
                }
                
                var fileContent = System.IO.File.ReadAllText(configPath);
                var lines = fileContent.Split('\n');
                
                string currentCategory = null;
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("//"))
                    {
                        var comment = trimmedLine.Substring(2).Trim();
                        var commentLower = comment.ToLower();
                        
                        if (commentLower.Contains("plane"))
                        {
                            currentCategory = "air";
                            DebugKaruzaVehicles($"[KARUZA CATEGORY] Found category comment: '{comment}' -> 'air'");
                        }
                        else if (commentLower.Contains("helicopter") || commentLower.Contains("copter"))
                        {
                            currentCategory = "air";
                            DebugKaruzaVehicles($"[KARUZA CATEGORY] Found category comment: '{comment}' -> 'air'");
                        }
                        else if (commentLower.Contains("car") || commentLower.Contains("bradley"))
                        {
                            currentCategory = "land";
                            DebugKaruzaVehicles($"[KARUZA CATEGORY] Found category comment: '{comment}' -> 'land'");
                        }
                        else if (commentLower.Contains("boat") || commentLower.Contains("water") || commentLower.Contains("submarine"))
                        {
                            currentCategory = "water";
                            DebugKaruzaVehicles($"[KARUZA CATEGORY] Found category comment: '{comment}' -> 'water'");
                        }
                        else if (commentLower.Contains("train") || commentLower.Contains("rail"))
                        {
                            currentCategory = "train";
                            DebugKaruzaVehicles($"[KARUZA CATEGORY] Found category comment: '{comment}' -> 'train'");
                        }
                        else if (commentLower.Contains("siege"))
                        {
                            currentCategory = "siege";
                            DebugKaruzaVehicles($"[KARUZA CATEGORY] Found category comment: '{comment}' -> 'siege'");
                        }
                        continue;
                    }
                    
                    if (currentCategory != null && line.Contains("PrefabPath = \"assets/custom/") && line.Contains(".prefab\""))
                    {
                        var start = line.IndexOf("\"assets/custom/") + "\"assets/custom/".Length;
                        var end = line.IndexOf(".prefab\"", start);
                        
                        if (end > start)
                        {
                            var vehicleType = line.Substring(start, end - start).ToLower();
                            _karuzaVehicleCategories[vehicleType] = currentCategory;
                            DebugKaruzaVehicles($"[KARUZA CATEGORY] Mapped '{vehicleType}' -> '{currentCategory}'");
                        }
                    }
                }
                
                DebugKaruzaVehicles($"[KARUZA CATEGORY] Loaded {_karuzaVehicleCategories.Count} vehicle category mappings");
            }
            catch (Exception ex)
            {
                PrintWarning($"Error loading vehicle categories from KaruzaVehicleItemManager.cs: {ex.Message}");
            }
        }

        private bool IsCustomVehicleAvailable(string vehicleType)
        {
            var lowerType = vehicleType.ToLower();
            if (_availableCustomVehicles.Contains(lowerType))
                return true;
            
            foreach (var v in _availableCustomVehicles)
            {
                if (v.Contains(lowerType) || lowerType.Contains(v))
                    return true;
            }
            
            return false;
        }
        
        private List<VehicleDisplayInfo> GetOwnedVehicles(BasePlayer player, string category = "all")
        {
            var vehicles = new List<VehicleDisplayInfo>();
            
            if (CorePlugin == null)
                return vehicles;
            
            var vehicleNames = ReadVehicleDataDirectly(player.userID);
            if (vehicleNames == null || vehicleNames.Count == 0)
                return vehicles;

            Dictionary<string, string> commandLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> displayNameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> purchasableVehicles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                var configPath = GetCoreConfigPath();
                if (System.IO.File.Exists(configPath))
                {
                    var jsonContent = System.IO.File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                    
                    var sectionsToSearch = new[] { "Modular Vehicle Settings", "Normal Vehicle Settings", "Train Vehicle Settings", "Custom Vehicle Settings" };
                    foreach (var topKey in sectionsToSearch)
                    {
                        if (!config.ContainsKey(topKey) || !(config[topKey] is Newtonsoft.Json.Linq.JObject topObj))
                            continue;

                        foreach (var property in topObj.Properties())
                        {
                            var normalizedName = NormalizeVehicleName(property.Name);
                            var vehicleData = property.Value as Newtonsoft.Json.Linq.JObject;
                            if (vehicleData == null)
                                continue;

                            var purchasableToken = vehicleData["Purchasable"];
                            bool isPurchasable = false;
                            if (purchasableToken != null)
                            {
                                try
                                {
                                    isPurchasable = purchasableToken.ToObject<bool>();
                                }
                                catch
                                {
                                    isPurchasable = false;
                                }
                            }
                            
                            if (!isPurchasable)
                            {
                                if (_config.EnableUIDebug)
                                    DebugUI($"[OWNED VEHICLES] Skipping {property.Name} - Purchasable is false");
                                continue;
                            }
                            
                            purchasableVehicles.Add(property.Name);
                            purchasableVehicles.Add(normalizedName);
                            
                            // Also add the internal vehicle type name (for VehicleLicence compatibility)
                            // This allows owned vehicles from data file (e.g., "Tumbler") to match config keys (e.g., "Tumbler Batmobile Vehicle")
                            var internalVehicleType = MapConfigKeyToVehicleType(property.Name);
                            string firstWordPascal = null;
                            
                            // Extract first word as potential match (some vehicles use just the first word as enum name)
                            // Example: "Tumbler Batmobile Vehicle" -> enum is "Tumbler", not "TumblerBatmobile"
                            var trimmed = property.Name.Trim();
                            if (trimmed.EndsWith(" Vehicle", StringComparison.OrdinalIgnoreCase))
                            {
                                trimmed = trimmed.Substring(0, trimmed.Length - 8).Trim();
                            }
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                var words = trimmed.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                                if (words.Length > 0)
                                {
                                    var firstWord = words[0];
                                    if (!string.IsNullOrEmpty(firstWord) && firstWord != property.Name)
                                    {
                                        // Capitalize first letter to match enum format
                                        firstWordPascal = char.ToUpperInvariant(firstWord[0]) + (firstWord.Length > 1 ? firstWord.Substring(1) : "");
                                    }
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(internalVehicleType) && internalVehicleType != property.Name)
                            {
                                purchasableVehicles.Add(internalVehicleType);
                                purchasableVehicles.Add(NormalizeVehicleName(internalVehicleType));
                            }
                            
                            if (!string.IsNullOrEmpty(firstWordPascal) && firstWordPascal != internalVehicleType && firstWordPascal != property.Name)
                            {
                                purchasableVehicles.Add(firstWordPascal);
                                purchasableVehicles.Add(NormalizeVehicleName(firstWordPascal));
                            }

                            var commandsArray = vehicleData["Commands"] as Newtonsoft.Json.Linq.JArray;
                            if (commandsArray != null && commandsArray.Count > 0)
                            {
                                var command = commandsArray[0].ToString().ToLower();
                                commandLookup[property.Name] = command;
                                commandLookup[normalizedName] = command;
                                
                                if (!string.IsNullOrEmpty(internalVehicleType) && internalVehicleType != property.Name)
                                {
                                    commandLookup[internalVehicleType] = command;
                                    commandLookup[NormalizeVehicleName(internalVehicleType)] = command;
                                }
                                
                                if (!string.IsNullOrEmpty(firstWordPascal) && firstWordPascal != internalVehicleType && firstWordPascal != property.Name)
                                {
                                    commandLookup[firstWordPascal] = command;
                                    commandLookup[NormalizeVehicleName(firstWordPascal)] = command;
                                }
                            }
                            
                            var displayName = vehicleData["Display Name"]?.ToString() ?? vehicleData["DisplayName"]?.ToString();
                            if (!string.IsNullOrEmpty(displayName))
                            {
                                displayNameLookup[property.Name] = displayName;
                                displayNameLookup[normalizedName] = displayName;
                                
                                if (!string.IsNullOrEmpty(internalVehicleType) && internalVehicleType != property.Name)
                                {
                                    displayNameLookup[internalVehicleType] = displayName;
                                    displayNameLookup[NormalizeVehicleName(internalVehicleType)] = displayName;
                                }
                                
                                if (!string.IsNullOrEmpty(firstWordPascal) && firstWordPascal != internalVehicleType && firstWordPascal != property.Name)
                                {
                                    displayNameLookup[firstWordPascal] = displayName;
                                    displayNameLookup[NormalizeVehicleName(firstWordPascal)] = displayName;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[OWNED VEHICLES] Error loading config: {ex.Message}");
            }
            
            foreach (var configSectionName in vehicleNames)
            {
                var normalizedName = NormalizeVehicleName(configSectionName);
                
                if (!purchasableVehicles.Contains(configSectionName) && !purchasableVehicles.Contains(normalizedName))
                {
                    if (_config.EnableUIDebug)
                        DebugUI($"[OWNED VEHICLES] Skipping owned vehicle {configSectionName} - Purchasable is false");
                    continue;
                }
                
                if (!commandLookup.TryGetValue(configSectionName, out var commandType) && 
                    !commandLookup.TryGetValue(normalizedName, out commandType))
                {
                    commandType = GetCommandFromConfigSection(configSectionName);
                }
                
                if (string.IsNullOrEmpty(commandType))
                {
                    DebugUI($"[OWNED VEHICLES] Could not get command for '{configSectionName}', skipping");
                    continue;
                }
                
                var vehicleEntity = CorePlugin?.Call("GetLicensedVehicle", player.userID.Get(), configSectionName);
                var isSpawned = vehicleEntity != null;
                
                if (!displayNameLookup.TryGetValue(configSectionName, out var displayName) && 
                    !displayNameLookup.TryGetValue(normalizedName, out displayName))
                {
                    displayName = GetDisplayNameFromConfigSection(configSectionName) ?? GetVehicleDisplayName(configSectionName);
                }
                
                var vehicleCategory = DetermineVehicleCategory(commandType, displayName);
                if (category != "all" && vehicleCategory != category)
                    continue;
                
                vehicles.Add(new VehicleDisplayInfo
                {
                    VehicleType = commandType,
                    DisplayName = displayName,
                    Image = null,
                    StatusInfo = isSpawned ? Lang("CurrentlySpawned", player) : Lang("AvailableToSpawn", player),
                    CanAfford = true,
                    IsSpawned = isSpawned,
                    ImageKey = GetImageKeyFromSection(configSectionName),
                    Category = vehicleCategory
                });
            }
            
            DebugUI($"[OWNED VEHICLES] Created {vehicles.Count} vehicle display items");
            return vehicles;
        }

        private string NormalizeVehicleName(string name)
        {
            var normalized = name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
            return normalized.EndsWith("vehicle") ? normalized.Substring(0, normalized.Length - 7) : normalized;
        }

        private string MapConfigKeyToVehicleType(string configKey)
        {
            if (string.IsNullOrEmpty(configKey))
                return configKey;

            if (CorePluginName == "VehicleLicence")
            {
                var trimmed = configKey.Trim();
                if (trimmed.EndsWith(" Vehicle", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - 8).Trim();
                }
                
                if (string.IsNullOrEmpty(trimmed))
                    return configKey;

                var words = trimmed.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                var sb = new StringBuilder();
                foreach (var word in words)
                {
                    if (word.Length > 0)
                    {
                        sb.Append(char.ToUpperInvariant(word[0]));
                        if (word.Length > 1)
                        {
                            sb.Append(word.Substring(1).ToLowerInvariant());
                        }
                    }
                }
                return sb.ToString();
            }

            return configKey;
        }

        private string GetCommandFromConfigSection(string configSectionName)
        {
            try
            {
                var configPath = GetCoreConfigPath();
                if (!System.IO.File.Exists(configPath))
                    return null;

                var jsonContent = System.IO.File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);

                var normalizedSearchName = configSectionName.Replace(" ", "").Replace("_", "").ToLowerInvariant();

                var sectionsToSearch = new[] { "Modular Vehicle Settings", "Normal Vehicle Settings", "Train Vehicle Settings", "Custom Vehicle Settings" };
                foreach (var topKey in sectionsToSearch)
                {
                    if (!config.ContainsKey(topKey) || !(config[topKey] is Newtonsoft.Json.Linq.JObject topObj))
                        continue;

                    foreach (var property in topObj.Properties())
                    {
                        var normalizedPropertyName = property.Name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        
                        if (normalizedPropertyName.EndsWith("vehicle"))
                            normalizedPropertyName = normalizedPropertyName.Substring(0, normalizedPropertyName.Length - 7);
                        
                        if (normalizedPropertyName == normalizedSearchName)
                        {
                            var vehicleData = property.Value as Newtonsoft.Json.Linq.JObject;
                            if (vehicleData != null)
                            {
                                var commandsArray = vehicleData["Commands"] as Newtonsoft.Json.Linq.JArray;
                                if (commandsArray != null && commandsArray.Count > 0)
                                {
                                    var firstCommand = commandsArray[0].ToString().ToLower();
                                    DebugUI($"[GET COMMAND] Found command '{firstCommand}' for config section '{configSectionName}' (matched property '{property.Name}')");
                                    return firstCommand;
                                }
                            }
                        }
                    }
                }

                DebugUI($"[GET COMMAND] No matching config section found for '{configSectionName}'");
                return null;
            }
            catch (Exception ex)
            {
                DebugUI($"[GET COMMAND] Error getting command for {configSectionName}: {ex.Message}");
                return null;
            }
        }

        private string GetDisplayNameFromConfigSection(string configSectionName)
        {
            try
            {
                var configPath = GetCoreConfigPath();
                if (!System.IO.File.Exists(configPath))
                    return null;

                var jsonContent = System.IO.File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);

                var normalizedSearchName = configSectionName.Replace(" ", "").Replace("_", "").ToLowerInvariant();

                var sectionsToSearch = new[] { "Modular Vehicle Settings", "Normal Vehicle Settings", "Train Vehicle Settings", "Custom Vehicle Settings" };
                foreach (var topKey in sectionsToSearch)
                {
                    if (!config.ContainsKey(topKey) || !(config[topKey] is Newtonsoft.Json.Linq.JObject topObj))
                        continue;

                    foreach (var property in topObj.Properties())
                    {
                        var normalizedPropertyName = property.Name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        
                        if (normalizedPropertyName.EndsWith("vehicle"))
                            normalizedPropertyName = normalizedPropertyName.Substring(0, normalizedPropertyName.Length - 7);
                        
                        if (normalizedPropertyName == normalizedSearchName)
                        {
                            var vehicleData = property.Value as Newtonsoft.Json.Linq.JObject;
                            if (vehicleData != null)
                            {
                                var displayName = vehicleData["Display Name"]?.ToString();
                                if (string.IsNullOrEmpty(displayName))
                                {
                                    displayName = vehicleData["DisplayName"]?.ToString();
                                }
                                if (!string.IsNullOrEmpty(displayName))
                                {
                                    DebugUI($"[GET DISPLAY] Found display name '{displayName}' for config section '{configSectionName}' (matched property '{property.Name}')");
                                    return displayName;
                                }
                                else
                                {
                                    DebugUI($"[GET DISPLAY] No display name found in config for '{configSectionName}' (property '{property.Name}')");
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                DebugUI($"[GET DISPLAY] Error getting display name for {configSectionName}: {ex.Message}");
                return null;
            }
        }

		private string GetVehicleDisplayName(string vehicleType)
        {
			var settings = CorePlugin?.Call("GetVehicleSettings", vehicleType);
            if (settings is Dictionary<string, object> vSettings && vSettings.ContainsKey("DisplayName"))
            {
                return vSettings["DisplayName"].ToString();
            }
			if (settings is Dictionary<string, object> vSettings2 && vSettings2.ContainsKey("Display Name"))
			{
				return vSettings2["Display Name"].ToString();
			}
            
            return FormatVehicleName(vehicleType);
        }

        private string GetVehicleImage(string imageKey, string displayTitle)
        {
            if (string.IsNullOrEmpty(imageKey) && string.IsNullOrEmpty(displayTitle))
                return null;

            var keysToTry = new List<string>();
            if (!string.IsNullOrEmpty(imageKey)) keysToTry.Add(imageKey);
            if (!string.IsNullOrEmpty(displayTitle))
            {
                var strippedTitle = StripVehicleWord(displayTitle).Trim();
                if (!string.IsNullOrEmpty(strippedTitle))
                {
                    keysToTry.Add(strippedTitle);
                    var toImageKey = ToImageKey(strippedTitle);
                    if (!string.IsNullOrEmpty(toImageKey) && toImageKey != strippedTitle) keysToTry.Add(toImageKey);
                }
                var normalizedTitle = GetImageKeyFromSection(displayTitle);
                if (!string.IsNullOrEmpty(normalizedTitle) && !keysToTry.Contains(normalizedTitle)) keysToTry.Add(normalizedTitle);
                var pascal = GetPascalImageKeyFromSection(displayTitle);
                if (!string.IsNullOrEmpty(pascal) && !keysToTry.Contains(pascal)) keysToTry.Add(pascal);
                var pascalWithUnderscores = GetPascalWithUnderscoresImageKey(displayTitle);
                if (!string.IsNullOrEmpty(pascalWithUnderscores) && !keysToTry.Contains(pascalWithUnderscores)) keysToTry.Add(pascalWithUnderscores);
                if (!string.IsNullOrEmpty(strippedTitle) && SpecialTitleToKey.TryGetValue(strippedTitle, out var fixedKeyTitle) && !keysToTry.Contains(fixedKeyTitle))
                    keysToTry.Add(fixedKeyTitle);
            }

            for (int i = 0; i < keysToTry.Count; i++)
            {
                var k = keysToTry[i];
                if (string.IsNullOrEmpty(k)) continue;
                if (_imageKeyToPngId.TryGetValue(k, out var id) && id != 0)
                    return id.ToString();
            }

            for (int i = 0; i < keysToTry.Count; i++)
            {
                var k = keysToTry[i];
                if (string.IsNullOrEmpty(k)) continue;
                if (RegisterLocalImageKey(k, out var newId))
                    return newId.ToString();
            }

            var distinctKeys = new HashSet<string>();
            foreach (var key in keysToTry)
            {
                if (!string.IsNullOrEmpty(key)) distinctKeys.Add(key);
            }
            DebugUI($"[IMG] No server image id found for '{displayTitle ?? imageKey}'. Tried: {string.Join(", ", distinctKeys)}");
            return null;
        }



        private string GetVehiclePriceInfo(Dictionary<string, object> vData, BasePlayer player = null)
        {
            if (vData.ContainsKey("price") && vData.ContainsKey("currency"))
            {
                return $"{vData["price"]} {vData["currency"]}";
            }
            return Lang("Free", player);
        }

        private string GetVehicleStatusInfo(Dictionary<string, object> vData, BasePlayer player = null)
        {
            if (vData.ContainsKey("isSpawned") && (bool)vData["isSpawned"])
            {
                return Lang("CurrentlySpawned", player);
            }
            
            if (vData.ContainsKey("cooldown") && vData["cooldown"] is int cooldown && cooldown > 0)
            {
                var minutes = cooldown / 60;
                var seconds = cooldown % 60;
                return Lang("Cooldown", player, $"{minutes:00}:{seconds:00}");
            }
            
            return Lang("Available", player);
        }


        private static bool IsURL(string uriName)
        {
            return Uri.TryCreate(uriName, UriKind.Absolute, out var uriResult) &&
                   (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private static string FormatVehicleName(string vehicleType)
        {
            if (string.IsNullOrEmpty(vehicleType)) return vehicleType;
            
            var formatted = vehicleType.Replace("_", " ").Replace("-", " ");
            var words = formatted.Split(' ');
            
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + (words[i].Length > 1 ? words[i].Substring(1).ToLower() : "");
                }
            }
            
            return string.Join(" ", words);
        }

        #endregion

        #region UI Classes 

        public class IColor
        {
            #region Fields

            [JsonProperty(PropertyName = "HEX")] public string HEX;

            [JsonProperty(PropertyName = "Opacity (0 - 100)")]
            public float Alpha;

            #endregion

            #region Public Methods

            [JsonIgnore] private string _cachedResult;

            [JsonIgnore] private bool _isCached;

            public string Get()
            {
                if (_isCached)
                    return _cachedResult;

                if (string.IsNullOrEmpty(HEX)) HEX = "#FFFFFF";

                var str = HEX.Trim('#');
                if (str.Length != 6)
                    throw new Exception(HEX);

                var r = byte.Parse(str.Substring(0, 2), NumberStyles.HexNumber);
                var g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
                var b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);

                _cachedResult = $"{(double) r / 255} {(double) g / 255} {(double) b / 255} {Alpha / 100}";
                _isCached = true;

                return _cachedResult;
            }

            #endregion

            #region Constructors

            public IColor()
            {
            }

            public IColor(string hex, float alpha = 100)
            {
                HEX = hex;
                Alpha = alpha;
            }

            public static IColor Create(string hex, float alpha = 100)
            {
                return new IColor(hex, alpha);
            }

            public static IColor CreateTransparent()
            {
                return new IColor("#000000", 0);
            }

            public static IColor CreateWhite()
            {
                return new IColor("#FFFFFF");
            }

            public static IColor CreateBlack()
            {
                return new IColor("#000000");
            }

            #endregion
        }

        public class InterfacePosition
        {
            #region Fields

            [JsonProperty(PropertyName = "AnchorMin")]
            public string AnchorMin = "0 0";

            [JsonProperty(PropertyName = "AnchorMax")]
            public string AnchorMax = "1 1";

            [JsonProperty(PropertyName = "OffsetMin")]
            public string OffsetMin = "0 0";

            [JsonProperty(PropertyName = "OffsetMax")]
            public string OffsetMax = "0 0";

            #endregion

            #region Cache

            [JsonIgnore] private CuiRectTransformComponent _position;

            #endregion

            #region Public Methods

            public CuiRectTransformComponent GetRectTransform()
            {
                if (_position != null) return _position;

                var rect = new CuiRectTransformComponent();

                if (!string.IsNullOrEmpty(AnchorMin))
                    rect.AnchorMin = AnchorMin;

                if (!string.IsNullOrEmpty(AnchorMax))
                    rect.AnchorMax = AnchorMax;

                if (!string.IsNullOrEmpty(OffsetMin))
                    rect.OffsetMin = OffsetMin;

                if (!string.IsNullOrEmpty(OffsetMax))
                    rect.OffsetMax = OffsetMax;

                _position = rect;

                return _position;
            }

            #endregion

            #region Constructors

            public InterfacePosition()
            {
            }

            public InterfacePosition(InterfacePosition other)
            {
                AnchorMin = other.AnchorMin;
                AnchorMax = other.AnchorMax;
                OffsetMin = other.OffsetMin;
                OffsetMax = other.OffsetMax;
            }

            public static InterfacePosition CreatePosition(float aMinX, float aMinY, float aMaxX, float aMaxY,
                float oMinX, float oMinY, float oMaxX, float oMaxY)
            {
                return new InterfacePosition
                {
                    AnchorMin = $"{aMinX} {aMinY}",
                    AnchorMax = $"{aMaxX} {aMaxY}",
                    OffsetMin = $"{oMinX} {oMinY}",
                    OffsetMax = $"{oMaxX} {oMaxY}"
                };
            }

            public static InterfacePosition CreatePosition(
                string anchorMin = "0 0",
                string anchorMax = "1 1",
                string offsetMin = "0 0",
                string offsetMax = "0 0")
            {
                return new InterfacePosition
                {
                    AnchorMin = anchorMin,
                    AnchorMax = anchorMax,
                    OffsetMin = offsetMin,
                    OffsetMax = offsetMax
                };
            }

            public static InterfacePosition CreateFullStretch()
            {
                return new InterfacePosition
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                };
            }

            public static InterfacePosition CreateCenter()
            {
                return new InterfacePosition
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                };
            }

            #endregion Constructors
        }

        public class TextSettings : InterfacePosition
        {
            #region Fields

            [JsonProperty(PropertyName = "Font Size")]
            public int FontSize = 12;

            [JsonProperty(PropertyName = "Is Bold?")]
            public bool IsBold;

            [JsonProperty(PropertyName = "Align")] [JsonConverter(typeof(StringEnumConverter))]
            public TextAnchor Align = TextAnchor.UpperLeft;

            [JsonProperty(PropertyName = "Color")] public IColor Color = IColor.CreateWhite();

            #endregion Fields

            #region Public Methods

            public CuiTextComponent GetTextComponent(string msg)
            {
                return new CuiTextComponent
                {
                    Text = msg ?? string.Empty,
                    FontSize = FontSize,
                    Font = IsBold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf",
                    Align = Align,
                    Color = Color.Get(),
                    VerticalOverflow = VerticalWrapMode.Overflow
                };
            }

            public CuiElement GetText(string msg,
                string parent,
                string name = null,
                string destroyUI = null)
            {
                if (string.IsNullOrEmpty(name))
                    name = CuiHelper.GetGuid();

                return new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    DestroyUi = destroyUI,
                    Components =
                    {
                        GetTextComponent(msg),
                        GetRectTransform()
                    }
                };
            }

            #endregion
        }

        public class ImageSettings : InterfacePosition
        {
            #region Fields

            [JsonProperty(PropertyName = "Image")] public string Image = string.Empty;

            [JsonProperty(PropertyName = "Color")] public IColor Color = IColor.CreateWhite();

            [JsonProperty(PropertyName = "Sprite")] public string Sprite = string.Empty;

            [JsonProperty(PropertyName = "Material")]
            public string Material = string.Empty;

            [JsonProperty(PropertyName = "CursorEnabled")]
            public bool CursorEnabled;

            [JsonProperty(PropertyName = "KeyboardEnabled")]
            public bool KeyboardEnabled;

            #endregion

            #region Cache

            [JsonIgnore] private CuiImageComponent _imageComponent;

            #endregion

            #region Private Methods

            private CuiImageComponent GetImageComponent()
            {
                if (_imageComponent == null)
                {
                    var image = new CuiImageComponent
                    {
                        Color = Color.Get()
                    };

                    if (!string.IsNullOrEmpty(Sprite))
                        image.Sprite = Sprite;

                    if (!string.IsNullOrEmpty(Material))
                        image.Material = Material;

                    _imageComponent = image;
                }

                return _imageComponent;
            }

            #endregion

            #region Public Methods

            public bool GetImageURL(out string url)
            {
                if (!string.IsNullOrWhiteSpace(Image) && IsURL(Image))
                {
                    url = Image;
                    return true;
                }

                url = null;
                return false;
            }

            public CuiElement GetImage(string parent,
                string name = null,
                string destroyUI = null)
            {
                if (string.IsNullOrEmpty(name))
                    name = CuiHelper.GetGuid();

                Instance?.DebugUI($"[IMG RENDER] Creating image element: {name} | Parent: {parent} | Image: {Image ?? "NONE"} | Color: {Color?.Get() ?? "NONE"}");

                var element = new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    DestroyUi = destroyUI,
                    Components = { GetRectTransform() }
                };
                
                if (!string.IsNullOrEmpty(Image))
                {
                    if (IsURL(Image))
                    {

                        element.Components.Add(new CuiRawImageComponent
                        {
                            Color = Color.Get(),
                            Url = Image
                        });
                        Instance?.DebugUI($"[IMG RENDER] Using RawImageComponent for URL: {Image}");
                    }
                    else if (Image.StartsWith("assets/"))
                    {

                        element.Components.Add(new CuiImageComponent
                        {
                            Color = Color.Get(),
                            Sprite = Image
                        });
                        Instance?.DebugUI($"[IMG RENDER] Using ImageComponent for asset: {Image}");
                    }
                    else if (uint.TryParse(Image, out var pngId) && pngId != 0)
                    {

                        var ownerId = CommunityEntity.ServerInstance?.net?.ID ?? default(NetworkableId);
                        if (ownerId != default(NetworkableId) && FileStorage.server.Get(pngId, FileStorage.Type.png, ownerId, 0u) != null)
                        {
                            element.Components.Add(new CuiRawImageComponent
                            {
                                Color = Color.Get(),
                                Png = pngId.ToString()
                            });
                            Instance?.DebugUI($"[IMG RENDER] Using RawImageComponent for PNG ID: {pngId}");
                        }
                        else
                        {
                            Instance?.DebugUI($"[IMG RENDER] Invalid PNG ID {pngId}, skipping image");

                        }
                    }
                    else
                    {

                        var head = Image.Length > 20 ? Image.Substring(0, 20) + "..." : Image;
                        Instance?.DebugUI($"[IMG RENDER] Skipping invalid image data (not URL, asset, or valid PNG ID): {head}");

                    }
                }
                else
                {

                    element.Components.Add(GetImageComponent());
                    Instance?.DebugUI($"[IMG RENDER] Using default image component");
                }

                if (CursorEnabled)
                    element.Components.Add(new CuiNeedsCursorComponent());

                if (KeyboardEnabled)
                    element.Components.Add(new CuiNeedsKeyboardComponent());

                return element;
            }

            #endregion

            #region Constructors

            public ImageSettings()
            {
            }

            public ImageSettings(string imageURL, IColor color, InterfacePosition position) : base(position)
            {
                Image = imageURL;
                Color = color;
            }

            #endregion
        }
        
        public class ButtonSettings : TextSettings
        {
            #region Fields

            [JsonProperty(PropertyName = "Button Color")]
            public IColor ButtonColor = IColor.CreateWhite();

            [JsonProperty(PropertyName = "Sprite")]
            public string Sprite = string.Empty;

            [JsonProperty(PropertyName = "Material")]
            public string Material = string.Empty;

            [JsonProperty(PropertyName = "Image")] public string Image = string.Empty;

            [JsonProperty(PropertyName = "Image Color")]
            public IColor ImageColor = IColor.CreateWhite();

            [JsonProperty(PropertyName = "Use custom image position settings?")]
            public bool UseCustomPositionImage = false;

            [JsonProperty(PropertyName = "Custom image position settings")]
            public InterfacePosition ImagePosition = CreateFullStretch();

            #endregion

            #region Public Methods
            
            public bool GetImageURL(out string url)
            {
                if (!string.IsNullOrWhiteSpace(Image) && IsURL(Image))
                {
                    url = Image;
                    return true;
                }

                url = null;
                return false;
            }
            
            public List<CuiElement> GetButton(
                string msg,
                string cmd,
                string parent,
                string name = null,
                string destroyUI = null,
                string close = null)
            {
                if (string.IsNullOrEmpty(name))
                    name = CuiHelper.GetGuid();

                var list = new List<CuiElement>();

                var btn = new CuiButtonComponent
                {
                    Color = ButtonColor.Get()
                };

                if (!string.IsNullOrEmpty(cmd))
                    btn.Command = cmd;

                if (!string.IsNullOrEmpty(close))
                    btn.Close = close;

                if (!string.IsNullOrEmpty(Sprite))
                    btn.Sprite = Sprite;

                if (!string.IsNullOrEmpty(Material))
                    btn.Material = Material;

                list.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    DestroyUi = destroyUI,
                    Components =
                    {
                        btn,
                        GetRectTransform()
                    }
                });

                if (!string.IsNullOrEmpty(Image))
                {
                    var child = new CuiElement
                    {
                        Parent = name,
                        Components = { }
                    };

                    if (Image.StartsWith("assets/"))
                    {
                        child.Components.Add(new CuiImageComponent { Color = ImageColor.Get(), Sprite = Image });
                    }
                    else if (uint.TryParse(Image, out var pngId) && pngId != 0)
                    {

                        var ownerId = CommunityEntity.ServerInstance?.net?.ID ?? default(NetworkableId);
                        if (ownerId != default(NetworkableId) && FileStorage.server.Get(pngId, FileStorage.Type.png, ownerId, 0u) != null)
                        {
                            child.Components.Add(new CuiRawImageComponent { Color = ImageColor.Get(), Png = pngId.ToString() });
                        }
                        else
                        {
                            Instance?.DebugUI($"[BUTTON] Invalid PNG ID {pngId}, skipping image");

                        }
                    }
                    else if (IsURL(Image))
                    {
                        child.Components.Add(new CuiRawImageComponent { Color = ImageColor.Get(), Url = Image });
                    }
                    else
                    {

                        Instance?.DebugUI($"[BUTTON] Skipping invalid image data (not URL or valid PNG ID): {Image.Substring(0, Math.Min(50, Image.Length))}");

                    }

                    if (child.Components.Count > 0)
                    {
                        child.Components.Add(UseCustomPositionImage && ImagePosition != null ? ImagePosition?.GetRectTransform() : new CuiRectTransformComponent());
                        list.Add(child);
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(msg))
                        list.Add(new CuiElement
                        {
                            Parent = name,
                            Components =
                            {
                                GetTextComponent(msg),
                                new CuiRectTransformComponent()
                            }
                        });
                }

                return list;
            }

            #endregion
        }

		private List<string> ReadVehicleDataDirectly(ulong playerId)
        {
            try
            {
				var dataPath = GetCoreDataPath();
                if (!System.IO.File.Exists(dataPath))
                {
                    return new List<string>();
                }
                
                var jsonContent = System.IO.File.ReadAllText(dataPath);
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                
                if (data.ContainsKey("playerData") && data["playerData"] is Newtonsoft.Json.Linq.JObject playerData)
                {
                    var playerIdStr = playerId.ToString();
                    if (playerData[playerIdStr] != null)
                    {
                        var vehicleData = playerData[playerIdStr] as Newtonsoft.Json.Linq.JObject;
                        if (vehicleData != null)
                        {
                            var vehicles = new List<string>();
                            foreach (var property in vehicleData.Properties())
                            {
                                vehicles.Add(property.Name);
                            }
                            DebugUI($"[DIRECT READ] Found {vehicles.Count} vehicles for player {playerId}: {string.Join(", ", vehicles)}");
                            return vehicles;
                        }
                    }
                }
                
                return new List<string>();
            }
            catch (Exception ex)
            {
                PrintWarning($"[DIRECT READ] Failed to read vehicle data: {ex.Message}");
                return new List<string>();
            }
        }

        #endregion

        #region Configuration

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData
            {
                ChatCommands = new List<string>
                {
                    "vehiclebuy",     
                    "l",
                    "vb", 
                    "rv",
                    "rustvehicles"
                },
				EnableTestMode = false,
				ImageThrottle = new ImageThrottleConfig { BatchSize = 16, IntervalSeconds = 0.2f },
                EnableKaruzaVehiclesDebug = false,
                EnableUIDebug = false,
                EnableServerPanelDebug = false,
                AutoRegisterImages = false
            };
            
            Config.WriteObject(_config, true);
            PrintWarning("Created new default configuration file");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<ConfigData>();
                if (_config == null) throw new Exception();
                
                bool configUpdated = false;
                
                if (_config.ChatCommands == null || _config.ChatCommands.Count == 0)
                {
                    _config.ChatCommands = new List<string> { "vgui" };
                    configUpdated = true;
                }
                
				if (_config.ImageThrottle == null)
                {
                    _config.ImageThrottle = new ImageThrottleConfig { BatchSize = 16, IntervalSeconds = 0.2f };
                    configUpdated = true;
                    PrintWarning("Added missing Image Throttle configuration");
                }
                
                var configPath = $"{Interface.Oxide.ConfigDirectory}/{Name}.json";
                if (System.IO.File.Exists(configPath))
                {
                    var rawJson = System.IO.File.ReadAllText(configPath);
					if (!rawJson.Contains("Enable Test Mode") || !rawJson.Contains("Image Throttle") || !rawJson.Contains("Auto Register Local Images") || !rawJson.Contains("KaruzaVehicles Debug") || !rawJson.Contains("UI Debug") || !rawJson.Contains("ServerPanel Debug"))
                    {
                        configUpdated = true;
                        PrintWarning("Configuration file is missing new properties, updating...");
                    }
                }

                if (configUpdated)
                {
                    PrintWarning("Configuration updated with missing properties");
                    SaveConfig();
                }
            }
            catch
            {
                PrintWarning("Configuration file is corrupt or missing, creating new one...");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        public class ConfigData
        {
            [JsonProperty("Chat Commands")]
            public List<string> ChatCommands { get; set; }

            [JsonProperty("Enable Test Mode")]
            public bool EnableTestMode { get; set; }

            [JsonProperty("Image Throttle")]
            public ImageThrottleConfig ImageThrottle { get; set; } = new ImageThrottleConfig();

			[JsonProperty("Auto Register Local Images")]
			public bool AutoRegisterImages { get; set; } = false;

            [JsonProperty("KaruzaVehicles Debug")]
            public bool EnableKaruzaVehiclesDebug { get; set; } = false;

            [JsonProperty("UI Debug")]
            public bool EnableUIDebug { get; set; } = false;

            [JsonProperty("ServerPanel Debug")]
            public bool EnableServerPanelDebug { get; set; } = false;
        }
        
        public class ImageThrottleConfig
        {
            [JsonProperty("Batch Size")] public int BatchSize { get; set; } = 16;
            [JsonProperty("Interval Seconds")] public float IntervalSeconds { get; set; } = 0.2f;
        }


        #endregion

        #region Data Classes

        public class VehicleDisplayInfo
        {
            public string VehicleType { get; set; }
            public string DisplayName { get; set; }
            public string Image { get; set; }
            public string StatusInfo { get; set; }
            public bool CanAfford { get; set; }
            public bool IsSpawned { get; set; }
            public string Category { get; set; } = "all";
            public string ImageKey { get; set; }
        }

        public class PlayerUISettings
        {
            [JsonProperty("UserID")]
            public ulong UserID { get; set; }

            [JsonProperty("Background Color")]
            public string BackgroundColor { get; set; }

            [JsonProperty("Transparency")]
            public float Transparency { get; set; } = 30f;
        }

        public class PlayerSettingsData
        {
            [JsonProperty("Player Settings")]
            public Dictionary<string, PlayerUISettings> PlayerSettings { get; set; } = new Dictionary<string, PlayerUISettings>();
        }

        #endregion

        #region ServerPanel Integration

        private CuiElementContainer API_OpenPlugin(BasePlayer player)
        {
            DebugServerPanel($"[SERVERPANEL] API_OpenPlugin called for player {player?.userID}");
            var container = new CuiElementContainer();
            
            try
            {
                if (player == null)
                {
                    DebugServerPanel("[SERVERPANEL] API_OpenPlugin: Player is null");
                    return container;
                }

                ClearImageQueue(player.userID);
                DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Cleared image queue for fresh start");

                if (player.IsDestroyed || player.net?.connection == null)
                {
                    DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Player {player.userID} is destroyed or disconnected");
                    return container;
                }

                if (CorePlugin == null)
                {
                    DebugServerPanel("[SERVERPANEL] API_OpenPlugin: CorePlugin is null");
                    player.ChatMessage(Lang("CorePluginNotLoaded", player));
                    return CreateErrorMessage(Lang("CorePluginNotLoaded", player));
                }

                if (!HasCoreUsePermission(player))
                {
                    DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Player {player.userID} doesn't have permission");
                    player.ChatMessage(Lang("NoPermission", player));
                    return CreateErrorMessage(Lang("NoPermission", player));
                }

                if (!_playerServerPanelView.ContainsKey(player.userID))
                {
                    _playerServerPanelView[player.userID] = "main";
                }
                var currentView = _playerServerPanelView[player.userID];
                DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Current view is '{currentView}'");
                
                switch (currentView)
                {
                    case "shop":
                        var category = _playerSelectedCategory.GetValueOrDefault(player.userID, "all");
                        DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Creating shop elements for category '{category}'");
                        container = CreateServerPanelShopElements(player, category);
                        break;
                    case "manage":
                        DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Creating manage elements");
                        container = CreateServerPanelManageElements(player);
                        break;
                    case "main":
                    default:
                        DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Creating main menu elements");
                        container = CreateServerPanelMainElements(player);
                        break;
                }
                
                if (container == null || container.Count == 0)
                {
                    DebugServerPanel("[SERVERPANEL] API_OpenPlugin: Container is null or empty, returning error message");
                    return CreateErrorMessage(Lang("FailedLoadShop", player));
                }
                
                DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Successfully created {container.Count} UI elements");
                return container;
            }
            catch (Exception ex)
            {
                DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Exception occurred: {ex.Message}");
                DebugServerPanel($"[SERVERPANEL] API_OpenPlugin: Stack trace: {ex.StackTrace}");
                PrintWarning($"[SERVERPANEL] API_OpenPlugin error: {ex.Message}");
                
                return CreateErrorMessage(Lang("ErrorLoadingShop", player, ex.Message));
            }
        }

        private CuiElementContainer CreateErrorMessage(string message)
        {
            var container = new CuiElementContainer();
            var errorText = new TextSettings
            {
                AnchorMin = "0.1 0.4",
                AnchorMax = "0.9 0.6",
                FontSize = 16,
                IsBold = true,
                Align = TextAnchor.MiddleCenter,
                Color = IColor.Create("#FF6B6B")
            };
            container.Add(errorText.GetText(message, "UI.Server.Panel.Content"));
            return container;
        }

        private CuiElementContainer CreateServerPanelMainElements(BasePlayer player)
        {
            DebugServerPanel($"[SERVERPANEL] CreateServerPanelMainElements: player={player?.userID}");
            var container = new CuiElementContainer();
            
            try
            {
                var titleText = new TextSettings
                {
                    AnchorMin = "0 0.9",
                    AnchorMax = "1 1",
                    FontSize = 26,
                    IsBold = true,
                    Align = TextAnchor.MiddleCenter,
                    Color = IColor.CreateWhite()
                };
                container.Add(titleText.GetText(Lang("TitleMain", player), "UI.Server.Panel.Content"));

                var vehicleCount = GetOwnedVehicleCount(player);
                var maxVehicles = GetMaxVehicles(player);
                var vehicleText = maxVehicles <= 0 ? $"{vehicleCount}" : $"{vehicleCount}/{maxVehicles}";
                
                var topRightInfoText = new TextSettings
                {
                    AnchorMin = "0.55 0.75",
                    AnchorMax = "0.90 0.87",
                    FontSize = 18,
                    Align = TextAnchor.UpperRight,
                    Color = IColor.CreateWhite()
                };
                container.Add(topRightInfoText.GetText($"{Lang("VehiclesCount", player, vehicleText)}\n{player.displayName}", "UI.Server.Panel.Content"));

                var balanceText = GetPlayerBalance(player);
                var leftInfoText = new TextSettings
                {
                    AnchorMin = "0.10 0.75",
                    AnchorMax = "0.45 0.87",
                    FontSize = 18,
                    Align = TextAnchor.UpperLeft,
                    Color = IColor.CreateWhite()
                };
                container.Add(leftInfoText.GetText($"{Lang("Balance", player)}\n{balanceText}", "UI.Server.Panel.Content"));

                var buyButtonWidth = 0.30f;
                var manageButtonWidth = 0.38f;
                var buttonHeight = 0.12f;
                var buttonSpacing = 0.05f;
                var centerX = 0.5f;
                var buttonY = 0.50f;
                var inwardOffset = 0.05f;
                
                var buyButton = new ButtonSettings
                {
                    AnchorMin = $"{centerX - buyButtonWidth - (buttonSpacing / 2) - 0.12 + inwardOffset} {buttonY - buttonHeight}",
                    AnchorMax = $"{centerX - (buttonSpacing / 2) - 0.12 + inwardOffset} {buttonY}",
                    ButtonColor = IColor.CreateTransparent(),
                    Color = IColor.CreateWhite(),
                    FontSize = 24,
                    IsBold = true,
                    Align = TextAnchor.MiddleCenter
                };
                container.AddRange(buyButton.GetButton(Lang("BuyVehicles", player), "vgui.serverpanel.view shop", "UI.Server.Panel.Content"));

                var manageButton = new ButtonSettings
                {
                    AnchorMin = $"{centerX + (buttonSpacing / 2) + 0.05 - inwardOffset} {buttonY - buttonHeight}",
                    AnchorMax = $"{centerX + manageButtonWidth + (buttonSpacing / 2) + 0.05 - inwardOffset} {buttonY}",
                    ButtonColor = IColor.CreateTransparent(),
                    Color = IColor.CreateWhite(),
                    FontSize = 24,
                    IsBold = true,
                    Align = TextAnchor.MiddleCenter
                };
                container.AddRange(manageButton.GetButton(Lang("ManageVehicles", player), "vgui.serverpanel.view manage", "UI.Server.Panel.Content"));

                DebugServerPanel($"[SERVERPANEL] CreateServerPanelMainElements: Created {container.Count} UI elements");
                return container;
            }
            catch (Exception ex)
            {
                DebugServerPanel($"[SERVERPANEL] CreateServerPanelMainElements: Exception occurred: {ex.Message}");
                PrintWarning($"[SERVERPANEL] CreateServerPanelMainElements error: {ex.Message}");
                
                var errorText = new TextSettings
                {
                    AnchorMin = "0.1 0.4",
                    AnchorMax = "0.9 0.6",
                    FontSize = 16,
                    IsBold = true,
                    Align = TextAnchor.MiddleCenter,
                    Color = IColor.Create("#FF6B6B")
                };
                container.Add(errorText.GetText(Lang("ErrorLoadingMain", player, ex.Message), "UI.Server.Panel.Content"));
                return container;
            }
        }

        private CuiElementContainer CreateServerPanelShopElements(BasePlayer player, string category = "all")
        {
            var container = new CuiElementContainer();
            
            try
            {

            var titleText = new TextSettings
            {
                AnchorMin = "0.05 0.92",
                AnchorMax = "0.95 0.98",
                FontSize = 16,
                IsBold = true,
                Align = TextAnchor.MiddleLeft,
                Color = IColor.Create("#E2DBD3", 90)
            };
            container.Add(titleText.GetText(Lang("TitleShop", player), "UI.Server.Panel.Content"));

            var backButton = new ButtonSettings
            {
                AnchorMin = "0.05 0.85",
                AnchorMax = "0.12 0.90",
                ButtonColor = IColor.Create("#6c757d", 70),
                Color = IColor.Create("#E2DBD3", 90),
                FontSize = 10,
                Align = TextAnchor.MiddleCenter
            };
            container.AddRange(backButton.GetButton(Lang("BackMain", player), "vgui.serverpanel.view main", "UI.Server.Panel.Content"));

            var categories = new[] { "all", "air", "land", "water", "train", "siege" };
            var buttonWidth = 0.10f; 
            var startX = 0.13f;
            var currentX = startX;

            for (int i = 0; i < categories.Length; i++)
            {
                var cat = categories[i];
                var isSelected = category == cat;

                var categoryButton = new ButtonSettings
                {
                    AnchorMin = $"{currentX} 0.85",
                    AnchorMax = $"{currentX + buttonWidth} 0.90",
                    ButtonColor = isSelected ? IColor.Create("#CF432D", 90) : IColor.Create("#2C2F31", 70),
                    Color = IColor.Create("#E2DBD3", 90),
                    FontSize = 10,
                    IsBold = true,
                    Align = TextAnchor.MiddleCenter
                };

                container.AddRange(categoryButton.GetButton(LangCategory(player, cat), $"vgui.serverpanel.shop {cat}", "UI.Server.Panel.Content"));
                currentX += buttonWidth + 0.01f;
            }

            List<VehicleDisplayInfo> vehicles;
            var cacheKey = $"{player.userID}_{category}";
            if (_cachedVehicleLists.TryGetValue(cacheKey, out vehicles) && 
                _cachedVehicleListPlayer.TryGetValue(cacheKey, out var cachedPlayerId) && 
                cachedPlayerId == player.userID)
            {
                DebugServerPanel($"[SERVERPANEL] Cache hit in CreateServerPanelShopElements for {cacheKey}, using cached list ({vehicles.Count} vehicles)");
            }
            else
            {
                DebugServerPanel($"[SERVERPANEL] Cache miss in CreateServerPanelShopElements for {cacheKey}, processing vehicles");
                vehicles = GetAvailableVehicles(player, category);
                _cachedVehicleLists[cacheKey] = vehicles;
                _cachedVehicleListPlayer[cacheKey] = player.userID;
                DebugServerPanel($"[SERVERPANEL] Populated cache in CreateServerPanelShopElements for {cacheKey} ({vehicles.Count} vehicles)");
            }
            var currentPage = _playerShopPage.GetValueOrDefault(player.userID, 0);
            var totalPages = (int)Math.Ceiling((double)vehicles.Count / VEHICLES_PER_PAGE);
            var startIndex = currentPage * VEHICLES_PER_PAGE;
            var pageVehicles = new List<VehicleDisplayInfo>();
            for (int i = startIndex; i < vehicles.Count && pageVehicles.Count < VEHICLES_PER_PAGE; i++)
            {
                pageVehicles.Add(vehicles[i]);
            }

            var cols = 4;
            var rows = 4;
            var gridStartX = 0.08f; 
            var gridStartY = 0.10f;
            var gridWidth = 0.82f;  
            var gridHeight = 0.74f; 

            var cellWidth = gridWidth / cols;
            var cellHeight = gridHeight / rows;
            var padding = 0.01f;

            for (int i = 0; i < Math.Min(pageVehicles.Count, VEHICLES_PER_PAGE); i++)
            {
                var vehicle = pageVehicles[i];
                var row = i / cols;
                var col = i % cols;

                var cellX = gridStartX + (col * cellWidth);
                var cellY = gridStartY + ((rows - 1 - row) * cellHeight);

                var cardName = $"vehicle_{i}"; 
                CreateServerPanelVehicleCardElements(player, container, vehicle, "UI.Server.Panel.Content", cellX + padding, cellY + padding,
                    cellWidth - (padding * 2), cellHeight - (padding * 2), cardName);
            }

            if (totalPages > 1)
            {
                var paginationY = 0.02f;
                var paginationHeight = 0.06f;

                if (currentPage > 0)
                {
                    var prevButton = new ButtonSettings
                    {
                        AnchorMin = $"0.35 {paginationY}",
                        AnchorMax = $"0.45 {paginationY + paginationHeight}",
                        ButtonColor = IColor.Create("#2C2F31", 70),
                        Color = IColor.Create("#FFFFFF", 90),
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter
                    };
                    container.AddRange(prevButton.GetButton("<", $"vgui.serverpanel.prevpage {category}", "UI.Server.Panel.Content"));
                }

                var pageText = new TextSettings
                {
                    AnchorMin = $"0.46 {paginationY}",
                    AnchorMax = $"0.54 {paginationY + paginationHeight}",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = IColor.Create("#DCDCDC", 50)
                };
                container.Add(pageText.GetText($"{currentPage + 1}/{totalPages}", "UI.Server.Panel.Content"));

                if (currentPage < totalPages - 1)
                {
                    var nextButton = new ButtonSettings
                    {
                        AnchorMin = $"0.55 {paginationY}",
                        AnchorMax = $"0.65 {paginationY + paginationHeight}",
                        ButtonColor = IColor.Create("#CF432D", 90),
                        Color = IColor.Create("#FFFFFF", 90),
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter
                    };
                    container.AddRange(nextButton.GetButton(">", $"vgui.serverpanel.nextpage {category}", "UI.Server.Panel.Content"));
                }
            }

            QueueServerPanelImages(player, pageVehicles);

            return container;
            }
            catch (Exception ex)
            {
                DebugServerPanel($"[SERVERPANEL] CreateServerPanelShopElements: Exception occurred: {ex.Message}");
                DebugServerPanel($"[SERVERPANEL] CreateServerPanelShopElements: Stack trace: {ex.StackTrace}");
                PrintWarning($"[SERVERPANEL] CreateServerPanelShopElements error: {ex.Message}");
                
                var errorText = new TextSettings
                {
                    AnchorMin = "0.1 0.4",
                    AnchorMax = "0.9 0.6",
                    FontSize = 16,
                    IsBold = true,
                    Align = TextAnchor.MiddleCenter,
                    Color = IColor.Create("#FF6B6B")
                };
                container.Add(errorText.GetText(Lang("ErrorLoadingVehicles", player, ex.Message), "UI.Server.Panel.Content"));
                return container;
            }
        }

        private void CreateServerPanelVehicleCardElements(BasePlayer player, CuiElementContainer container, VehicleDisplayInfo vehicle,
            string parent, float x, float y, float width, float height, string cardName)
        {
            var cardPanel = new ImageSettings
            {
                AnchorMin = $"{x} {y}",
                AnchorMax = $"{x + width} {y + height}",
                Color = IColor.CreateTransparent()
            };
            container.Add(cardPanel.GetImage(parent, cardName));

            var imageHeight = height * 0.55f; 
            var imageY = y + height - imageHeight; 

            var imagePanel = new ImageSettings
            {
                AnchorMin = $"{x + 0.01f} {imageY}",
                AnchorMax = $"{x + width - 0.01f} {y + height - 0.01f}",
                Color = IColor.Create("#333333", 0f) 
            };
            container.Add(imagePanel.GetImage(cardName, cardName + "_bg"));

            var nameText = new TextSettings
            {
                AnchorMin = "0.05 0.30", 
                AnchorMax = "0.95 0.40",
                FontSize = 10,
                IsBold = true,
                Align = TextAnchor.MiddleCenter,
                Color = IColor.Create("#E2DBD3", 90)
            };
            container.Add(nameText.GetText(vehicle.DisplayName, cardName));

            var statusColor = vehicle.CanAfford ? IColor.Create("#90EE90") : IColor.Create("#FF6B6B");
            var statusText = new TextSettings
            {
                AnchorMin = "0.05 0.20", 
                AnchorMax = "0.95 0.28",
                FontSize = 8,
                Align = TextAnchor.MiddleCenter,
                Color = statusColor
            };
            container.Add(statusText.GetText(vehicle.StatusInfo, cardName));


            var buyButton = new ButtonSettings
            {
                AnchorMin = "0.40 0.02", 
                AnchorMax = "0.60 0.18",
                ButtonColor = vehicle.CanAfford ? IColor.Create("#4a90e2") : IColor.Create("#666666"),
                Color = IColor.CreateWhite(),
                FontSize = 8,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };

            var buttonText = vehicle.IsSpawned ? Lang("Spawned", player) : Lang("Buy", player);
            var command = vehicle.CanAfford && !vehicle.IsSpawned ? $"vgui.buy {vehicle.VehicleType}" : string.Empty;

            container.AddRange(buyButton.GetButton(buttonText, command, cardName));
        }

        private CuiElementContainer CreateServerPanelManageElements(BasePlayer player)
        {
            DebugServerPanel($"[SERVERPANEL] CreateServerPanelManageElements: player={player?.userID}");
            var container = new CuiElementContainer();
            
            try
            {
                var titleText = new TextSettings
                {
                    AnchorMin = "0.05 0.92",
                    AnchorMax = "0.95 0.98",
                    FontSize = 16,
                    IsBold = true,
                    Align = TextAnchor.MiddleLeft,
                    Color = IColor.Create("#E2DBD3", 90)
                };
                container.Add(titleText.GetText(Lang("TitleManage", player), "UI.Server.Panel.Content"));

                var backButton = new ButtonSettings
                {
                    AnchorMin = "0.05 0.85",
                    AnchorMax = "0.12 0.90",
                    ButtonColor = IColor.Create("#6c757d", 70),
                    Color = IColor.Create("#E2DBD3", 90),
                    FontSize = 10,
                    Align = TextAnchor.MiddleCenter
                };
                container.AddRange(backButton.GetButton(Lang("BackMain", player), "vgui.serverpanel.view main", "UI.Server.Panel.Content"));

                var category = _playerSelectedManageCategory.GetValueOrDefault(player.userID, "all");
                var categories = new[] { "all", "air", "land", "water", "train", "siege" };
                var buttonWidth = 0.10f; 
                var startX = 0.13f;
                var currentX = startX;

                for (int i = 0; i < categories.Length; i++)
                {
                    var cat = categories[i];
                    var isSelected = cat == category;

                    var categoryButton = new ButtonSettings
                    {
                        AnchorMin = $"{currentX} 0.85",
                        AnchorMax = $"{currentX + buttonWidth} 0.90",
                        ButtonColor = isSelected ? IColor.Create("#CF432D", 90) : IColor.Create("#2C2F31", 70),
                        Color = IColor.Create("#E2DBD3", 90),
                        FontSize = 10,
                        IsBold = true,
                        Align = TextAnchor.MiddleCenter
                    };

                    container.AddRange(categoryButton.GetButton(LangCategory(player, cat), $"vgui.serverpanel.manage.category {cat}", "UI.Server.Panel.Content"));
                    currentX += buttonWidth + 0.01f;
                }

                var allOwnedVehicles = GetOwnedVehicles(player);
                var ownedVehicles = new List<VehicleDisplayInfo>();
                foreach (var vehicle in allOwnedVehicles)
                {
                    if (category == "all" || vehicle.Category == category)
                    {
                        ownedVehicles.Add(vehicle);
                    }
                }
                var currentPage = _playerManagePage.GetValueOrDefault(player.userID, 0);
                var totalPages = (int)Math.Ceiling((double)ownedVehicles.Count / VEHICLES_PER_PAGE);
                var startIndex = currentPage * VEHICLES_PER_PAGE;
                var pageVehicles = new List<VehicleDisplayInfo>();
                for (int i = startIndex; i < ownedVehicles.Count && pageVehicles.Count < VEHICLES_PER_PAGE; i++)
                {
                    pageVehicles.Add(ownedVehicles[i]);
                }

                var cols = 4;
                var rows = 4;
                var gridStartX = 0.08f;
                var gridStartY = 0.10f;
                var gridWidth = 0.82f;
                var gridHeight = 0.74f;

                var cellWidth = gridWidth / cols;
                var cellHeight = gridHeight / rows;
                var padding = 0.01f;

                if (pageVehicles.Count == 0)
                {
                    var noVehiclesText = new TextSettings
                    {
                        AnchorMin = "0.2 0.4",
                        AnchorMax = "0.8 0.6",
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter,
                        Color = IColor.Create("#888888", 90)
                    };
                    container.Add(noVehiclesText.GetText(Lang("NoVehiclesCategory", player), "UI.Server.Panel.Content"));
                }
                else
                {
                    for (int i = 0; i < Math.Min(pageVehicles.Count, VEHICLES_PER_PAGE); i++)
                    {
                        var vehicle = pageVehicles[i];
                        var row = i / cols;
                        var col = i % cols;

                        var cellX = gridStartX + (col * cellWidth);
                        var cellY = gridStartY + ((rows - 1 - row) * cellHeight);

                        var cardName = $"vehicle_{i}";
                        CreateServerPanelManageVehicleCardElements(player, container, vehicle, "UI.Server.Panel.Content", 
                            cellX + padding, cellY + padding, cellWidth - (padding * 2), cellHeight - (padding * 2), cardName);
                    }

                    QueueServerPanelImages(player, pageVehicles);
                }

                if (totalPages > 1)
                {
                    var paginationY = 0.02f;
                    var paginationHeight = 0.06f;

                    if (currentPage > 0)
                    {
                        var prevButton = new ButtonSettings
                        {
                            AnchorMin = $"0.35 {paginationY}",
                            AnchorMax = $"0.45 {paginationY + paginationHeight}",
                            ButtonColor = IColor.Create("#2C2F31", 70),
                            Color = IColor.Create("#FFFFFF", 90),
                            FontSize = 12,
                            Align = TextAnchor.MiddleCenter
                        };
                        container.AddRange(prevButton.GetButton("<", $"vgui.serverpanel.manage.prevpage {category}", "UI.Server.Panel.Content"));
                    }

                    var pageText = new TextSettings
                    {
                        AnchorMin = $"0.46 {paginationY}",
                        AnchorMax = $"0.54 {paginationY + paginationHeight}",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = IColor.Create("#DCDCDC", 50)
                    };
                    container.Add(pageText.GetText($"{currentPage + 1}/{totalPages}", "UI.Server.Panel.Content"));

                    if (currentPage < totalPages - 1)
                    {
                        var nextButton = new ButtonSettings
                        {
                            AnchorMin = $"0.55 {paginationY}",
                            AnchorMax = $"0.65 {paginationY + paginationHeight}",
                            ButtonColor = IColor.Create("#CF432D", 90),
                            Color = IColor.Create("#FFFFFF", 90),
                            FontSize = 12,
                            Align = TextAnchor.MiddleCenter
                        };
                        container.AddRange(nextButton.GetButton(">", $"vgui.serverpanel.manage.nextpage {category}", "UI.Server.Panel.Content"));
                    }
                }

                return container;
            }
            catch (Exception ex)
            {
                DebugServerPanel($"[SERVERPANEL] CreateServerPanelManageElements: Exception occurred: {ex.Message}");
                DebugServerPanel($"[SERVERPANEL] CreateServerPanelManageElements: Stack trace: {ex.StackTrace}");
                PrintWarning($"[SERVERPANEL] CreateServerPanelManageElements error: {ex.Message}");
                
                var errorText = new TextSettings
                {
                    AnchorMin = "0.1 0.4",
                    AnchorMax = "0.9 0.6",
                    FontSize = 16,
                    IsBold = true,
                    Align = TextAnchor.MiddleCenter,
                    Color = IColor.Create("#FF6B6B")
                };
                container.Add(errorText.GetText(Lang("ErrorLoadingVehicles", player, ex.Message), "UI.Server.Panel.Content"));
                return container;
            }
        }

        private void CreateServerPanelManageVehicleCardElements(BasePlayer player, CuiElementContainer container, VehicleDisplayInfo vehicle,
            string parent, float x, float y, float width, float height, string cardName)
        {
            var cardPanel = new ImageSettings
            {
                AnchorMin = $"{x} {y}",
                AnchorMax = $"{x + width} {y + height}",
                Color = IColor.CreateTransparent()
            };
            container.Add(cardPanel.GetImage(parent, cardName));

            var imageHeight = height * 0.55f; 
            var imageY = y + height - imageHeight; 

            var imagePanel = new ImageSettings
            {
                AnchorMin = $"{x + 0.01f} {imageY}",
                AnchorMax = $"{x + width - 0.01f} {y + height - 0.01f}",
                Color = IColor.Create("#333333", 0f) 
            };
            container.Add(imagePanel.GetImage(cardName, cardName + "_bg"));

            var nameText = new TextSettings
            {
                AnchorMin = "0.05 0.30", 
                AnchorMax = "0.95 0.40",
                FontSize = 10,
                IsBold = true,
                Align = TextAnchor.MiddleCenter,
                Color = IColor.Create("#E2DBD3", 90)
            };
            container.Add(nameText.GetText(vehicle.DisplayName, cardName));

            var statusText = new TextSettings
            {
                AnchorMin = "0.05 0.16", 
                AnchorMax = "0.95 0.27",
                FontSize = 8,
                Align = TextAnchor.MiddleCenter,
                Color = IColor.Create("#87CEEB", 90)
            };
            container.Add(statusText.GetText(vehicle.StatusInfo, cardName));

            var buttonWidth = 0.18f;
            var buttonHeight = 0.12f;
            var buttonY = 0.02f;
            var buttonSpacing = 0.01f;
            
            var buttonCount = vehicle.IsSpawned ? 2 : 3;
            var totalButtonWidth = (buttonWidth * buttonCount) + (buttonSpacing * (buttonCount - 1));
            var startX = (1.0f - totalButtonWidth) / 2.0f;

            var recallButton = new ButtonSettings
            {
                AnchorMin = $"{startX} {buttonY}",
                AnchorMax = $"{startX + buttonWidth} {buttonY + buttonHeight}",
                ButtonColor = IColor.Create("#4a90e2", 70),
                Color = IColor.CreateWhite(),
                FontSize = 7,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };
            container.AddRange(recallButton.GetButton(Lang("Recall", player), $"vgui.recall {vehicle.VehicleType}", cardName));

            var pickupButton = new ButtonSettings
            {
                AnchorMin = $"{startX + buttonWidth + buttonSpacing} {buttonY}",
                AnchorMax = $"{startX + (buttonWidth * 2) + buttonSpacing} {buttonY + buttonHeight}",
                ButtonColor = IColor.Create("#5cb85c", 70),
                Color = IColor.CreateWhite(),
                FontSize = 7,
                IsBold = true,
                Align = TextAnchor.MiddleCenter
            };
            container.AddRange(pickupButton.GetButton(Lang("Pickup", player), $"vgui.pickup {vehicle.VehicleType}", cardName));

            if (!vehicle.IsSpawned)
            {
                var spawnButton = new ButtonSettings
                {
                    AnchorMin = $"{startX + (buttonWidth * 2) + (buttonSpacing * 2)} {buttonY}",
                    AnchorMax = $"{startX + (buttonWidth * 3) + (buttonSpacing * 2)} {buttonY + buttonHeight}",
                    ButtonColor = IColor.Create("#f0ad4e", 70),
                    Color = IColor.CreateWhite(),
                    FontSize = 7,
                    IsBold = true,
                    Align = TextAnchor.MiddleCenter
                };
                container.AddRange(spawnButton.GetButton(Lang("Spawn", player), $"vgui.spawn {vehicle.VehicleType}", cardName));
            }
        }

        private void QueueServerPanelImages(BasePlayer player, List<VehicleDisplayInfo> pageVehicles)
        {
            DebugServerPanel($"[SERVERPANEL] QueueServerPanelImages: player={player?.userID}, vehicles={pageVehicles?.Count ?? 0}");
            if (player == null || pageVehicles == null || pageVehicles.Count == 0)
            {
                DebugServerPanel("[SERVERPANEL] QueueServerPanelImages: Early return - player or vehicles null/empty");
                return;
            }

            ClearImageQueue(player.userID);

            var q = new Queue<ImageTask>();
            for (int i = 0; i < Math.Min(pageVehicles.Count, VEHICLES_PER_PAGE); i++)
            {
                var v = pageVehicles[i];
                q.Enqueue(new ImageTask
                {
                    Parent = $"vehicle_{i}",
                    AnchorMin = "0.1 0.42",
                    AnchorMax = "0.9 0.97",
                    ImageKey = v.ImageKey,
                    ImageTitle = v.DisplayName
                });
                DebugUI($"[IMGQ][SP] Enqueued '{v.DisplayName}' key='{v.ImageKey}' under vehicle_{i}");
                DebugServerPanel($"[SERVERPANEL] QueueServerPanelImages: Enqueued image for '{v.DisplayName}' (key: '{v.ImageKey}')");
            }

            _imageQueues[player.userID] = q;
            _imageQueueActive.Add(player.userID);
            DebugServerPanel($"[SERVERPANEL] QueueServerPanelImages: Queued {q.Count} images, starting processing");
            timer.Once(0.5f, () => ProcessImageQueue(player.userID));
        }

        #endregion

        private void RegisterLocalImages()
        {
            try
            {
                var imagesDir = System.IO.Path.Combine(Interface.Oxide.DataDirectory, "RustVehiclesGUI", "images");
                if (!System.IO.Directory.Exists(imagesDir))
                {
                    DebugUI($"[IMG] Local images directory not found: {imagesDir}");
                    return;
                }

                var allFiles = System.IO.Directory.EnumerateFiles(imagesDir, "*", System.IO.SearchOption.TopDirectoryOnly);
                var files = new List<string>();
                foreach (var file in allFiles)
                {
                    if (string.Equals(System.IO.Path.GetExtension(file), ".png", StringComparison.OrdinalIgnoreCase))
                        files.Add(file);
                }
                var filesArray = files.ToArray();
                if (filesArray == null || filesArray.Length == 0)
                {
                    DebugUI("[IMG] No local images found to register.");
                    return;
                }

                int registered = 0;
                foreach (var file in filesArray)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(name)) continue;

                    var lowerKey = ToImageKey(name);
                    var pascalKey = GetPascalImageKeyFromSection(name);

                    var bytes = System.IO.File.ReadAllBytes(file);
                    var ownerIdNullable = CommunityEntity.ServerInstance?.net?.ID;
                    var owner = ownerIdNullable ?? default(NetworkableId);
                    if (owner == default(NetworkableId))
                    {
                        DebugUI("[IMG] CommunityEntity.ServerInstance not ready; skipping local image registration.");
                        continue;
                    }

                    var id = FileStorage.server.Store(bytes, FileStorage.Type.png, owner);
                    if (id != 0)
                    {
                        _imageKeyToPngId[lowerKey] = id;
                        if (!string.Equals(pascalKey, lowerKey, StringComparison.Ordinal))
                            _imageKeyToPngId[pascalKey] = id;
                        registered++;
                        DebugUI($"[IMG] Registered '{name}' as id {id} for keys [{lowerKey}{(pascalKey!=lowerKey?", "+pascalKey:"")}] ");
                    }
                }
                DebugUI($"[IMG] Registered {registered} images into FileStorage.");
            }
            catch (Exception ex)
            {
                PrintWarning($"[IMG] Failed to auto-register local images: {ex.Message}");
            }
        }

        private bool RegisterLocalImageKey(string key, out uint id)
        {
            id = 0;
            try
            {
                if (string.IsNullOrEmpty(key)) return false;
                if (_imageKeyToPngId.TryGetValue(key, out id) && id != 0) return true;

                var imagesDir = System.IO.Path.Combine(Interface.Oxide.DataDirectory, "RustVehiclesGUI", "images");
                if (!System.IO.Directory.Exists(imagesDir)) return false;

                var candidates = new List<string>
                {
                    System.IO.Path.Combine(imagesDir, key + ".png"),
                    System.IO.Path.Combine(imagesDir, ToImageKey(key) + ".png"),
                    System.IO.Path.Combine(imagesDir, GetPascalImageKeyFromSection(key) + ".png"),
                    System.IO.Path.Combine(imagesDir, GetPascalWithUnderscoresImageKey(key) + ".png")
                };

                string found = null;
                foreach (var c in candidates)
                {
                    if (System.IO.File.Exists(c)) { found = c; break; }
                }
                if (string.IsNullOrEmpty(found)) return false;

                var bytes = System.IO.File.ReadAllBytes(found);
                var ownerIdNullable2 = CommunityEntity.ServerInstance?.net?.ID;
                var owner = ownerIdNullable2 ?? default(NetworkableId);
                if (owner == default(NetworkableId)) return false;

                id = FileStorage.server.Store(bytes, FileStorage.Type.png, owner);
                if (id == 0) return false;

                _imageKeyToPngId[key] = id;
                _imageKeyToPngId[ToImageKey(key)] = id;
                var pascal = GetPascalImageKeyFromSection(key);
                if (!string.IsNullOrEmpty(pascal)) _imageKeyToPngId[pascal] = id;
                var pascalWithUnderscores = GetPascalWithUnderscoresImageKey(key);
                if (!string.IsNullOrEmpty(pascalWithUnderscores)) _imageKeyToPngId[pascalWithUnderscores] = id;
                return true;
            }
            catch
            {
                id = 0;
                return false;
            }
        }

        #region Player Settings Persistence

        private string GetPlayerSettingsPath()
        {
            var dataDir = System.IO.Path.Combine(Interface.Oxide.DataDirectory, "RustVehiclesGUI", "players");
            if (!System.IO.Directory.Exists(dataDir))
            {
                System.IO.Directory.CreateDirectory(dataDir);
            }
            return System.IO.Path.Combine(dataDir, "playerSettings.json");
        }

        private void LoadPlayerSettings()
        {
            try
            {
                var settingsPath = GetPlayerSettingsPath();
                if (!System.IO.File.Exists(settingsPath))
                {
                    DebugUI("[SETTINGS] Player settings file not found, starting with empty settings");
                    return;
                }

                var jsonContent = System.IO.File.ReadAllText(settingsPath);
                var data = JsonConvert.DeserializeObject<PlayerSettingsData>(jsonContent);

                if (data?.PlayerSettings != null)
                {
                    foreach (var kvp in data.PlayerSettings)
                    {
                        if (ulong.TryParse(kvp.Key, out var userId) && kvp.Value != null)
                        {
                            _playerSettings[userId] = kvp.Value;
                            
                            if (!string.IsNullOrEmpty(kvp.Value.BackgroundColor))
                            {
                                _playerBackgroundColor[userId] = kvp.Value.BackgroundColor;
                            }
                            
                            if (kvp.Value.Transparency > 0)
                            {
                                _playerTransparency[userId] = kvp.Value.Transparency;
                            }
                        }
                    }
                    DebugUI($"[SETTINGS] Loaded UI settings for {_playerSettings.Count} players");
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"[SETTINGS] Error loading player settings: {ex.Message}");
            }
        }

        private void LoadPlayerSettings(ulong userId)
        {
            if (_playerSettings.ContainsKey(userId))
                return;

            try
            {
                var settingsPath = GetPlayerSettingsPath();
                if (!System.IO.File.Exists(settingsPath))
                    return;

                var jsonContent = System.IO.File.ReadAllText(settingsPath);
                var data = JsonConvert.DeserializeObject<PlayerSettingsData>(jsonContent);

                if (data?.PlayerSettings != null)
                {
                    var userIdStr = userId.ToString();
                    if (data.PlayerSettings.TryGetValue(userIdStr, out var settings) && settings != null)
                    {
                        _playerSettings[userId] = settings;
                        
                        if (!string.IsNullOrEmpty(settings.BackgroundColor))
                        {
                            _playerBackgroundColor[userId] = settings.BackgroundColor;
                        }
                        
                        if (settings.Transparency > 0)
                        {
                            _playerTransparency[userId] = settings.Transparency;
                        }
                        
                        DebugUI($"[SETTINGS] Loaded UI settings for player {userId}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugUI($"[SETTINGS] Error loading settings for player {userId}: {ex.Message}");
            }
        }

        private void SavePlayerSettings(ulong userId, bool immediate = false)
        {
            if (immediate)
            {
                if (_pendingSaveTimers.TryGetValue(userId, out var timer))
                {
                    timer.Destroy();
                    _pendingSaveTimers.Remove(userId);
                }
                
                SavePlayerSettingsInternal(userId);
                return;
            }
            
            if (_pendingSaveTimers.TryGetValue(userId, out var existingTimer))
            {
                existingTimer.Destroy();
            }
            
            var newTimer = timer.Once(SAVE_DEBOUNCE_DELAY, () =>
            {
                SavePlayerSettingsInternal(userId);
                _pendingSaveTimers.Remove(userId);
            });
            
            _pendingSaveTimers[userId] = newTimer;
        }

        private void SavePlayerSettingsInternal(ulong userId)
        {
            try
            {
                var settingsPath = GetPlayerSettingsPath();
                PlayerSettingsData data;

                if (System.IO.File.Exists(settingsPath))
                {
                    var jsonContent = System.IO.File.ReadAllText(settingsPath);
                    data = JsonConvert.DeserializeObject<PlayerSettingsData>(jsonContent);
                    if (data == null)
                    {
                        data = new PlayerSettingsData();
                    }
                }
                else
                {
                    data = new PlayerSettingsData();
                }

                if (data.PlayerSettings == null)
                {
                    data.PlayerSettings = new Dictionary<string, PlayerUISettings>();
                }

                var userIdStr = userId.ToString();
                if (!_playerSettings.TryGetValue(userId, out var playerSettings))
                {
                    playerSettings = new PlayerUISettings { UserID = userId };
                    _playerSettings[userId] = playerSettings;
                }

                if (_playerBackgroundColor.TryGetValue(userId, out var color))
                {
                    playerSettings.BackgroundColor = color;
                }
                else
                {
                    playerSettings.BackgroundColor = null;
                }

                if (_playerTransparency.TryGetValue(userId, out var transparency))
                {
                    playerSettings.Transparency = transparency;
                }
                else
                {
                    playerSettings.Transparency = DEFAULT_TRANSPARENCY;
                }

                data.PlayerSettings[userIdStr] = playerSettings;

                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                System.IO.File.WriteAllText(settingsPath, json);
                
                DebugUI($"[SETTINGS] Saved UI settings for player {userId}");
            }
            catch (Exception ex)
            {
                PrintWarning($"[SETTINGS] Error saving settings for player {userId}: {ex.Message}");
            }
        }

        #endregion
    }
}
