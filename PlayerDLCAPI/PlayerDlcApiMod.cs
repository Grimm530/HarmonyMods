using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerDlcApiHarmony
{
    /// <summary>
    /// Oxide-free DLC and paid-skin ownership API.
    /// Uses Rust's ownership checks and only maintains the lookup indexes Rust does not expose.
    /// </summary>
    public sealed class PlayerDlcApiMod : IHarmonyModHooks
    {
        public const int VersionMajor = 1;
        public const int VersionMinor = 7;
        public const int VersionPatch = 0;

        public const string ApiTypeKey = "PlayerDlcApi_ApiType";
        public const string GenerationKey = "PlayerDlcApi_Generation";
        public const string ReadyCallbacksKey = "PlayerDlcApi_ReadyCallbacks";

        private const string WorkshopIdProperty = "workshopid";

        private readonly Dictionary<int, ItemSkinDirectory.Skin> _contentIdToSkin =
            new Dictionary<int, ItemSkinDirectory.Skin>();
        private readonly Dictionary<ulong, int> _workshopToContentId =
            new Dictionary<ulong, int>();
        private readonly Dictionary<int, int> _itemIdToContentId =
            new Dictionary<int, int>();
        private readonly Dictionary<string, int> _shortnameToContentId =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, SteamDLCItem> _dlcById =
            new Dictionary<int, SteamDLCItem>();
        private readonly Dictionary<int, int> _itemIdToDlcId =
            new Dictionary<int, int>();
        private readonly Dictionary<string, int> _shortnameToDlcId =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _redirectedShortnameToBaseItem =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _redirectedItemIdToBaseItem =
            new Dictionary<int, int>();
        private readonly Dictionary<string, int> _redirectedShortnameToContentId =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _redirectedIdToContentId =
            new Dictionary<int, int>();

        private GameObject _runnerObject;
        private bool _initialized;
        private bool _subscribed;

        public static PlayerDlcApiMod Instance { get; private set; }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            PublishApi();
            Subscribe();
            EnsureRunner();
            Debug.Log("[PlayerDLCAPI] OK: Loaded v1.7.0; waiting for Rust inventory definitions.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            Unsubscribe();
            _initialized = false;
            ClearIndexes();

            if (_runnerObject != null)
            {
                UnityEngine.Object.Destroy(_runnerObject);
                _runnerObject = null;
            }

            if (ReferenceEquals(AppDomain.CurrentDomain.GetData(ApiTypeKey), typeof(PlayerDlcApiMod)))
                AppDomain.CurrentDomain.SetData(ApiTypeKey, null);

            Instance = null;
            Debug.Log("[PlayerDLCAPI] OK: Unloaded.");
        }

        private void PublishApi()
        {
            AppDomain domain = AppDomain.CurrentDomain;
            int generation = 0;
            object current = domain.GetData(GenerationKey);
            if (current is int value)
                generation = value;

            domain.SetData(GenerationKey, generation + 1);
            domain.SetData(ApiTypeKey, typeof(PlayerDlcApiMod));

            if (!(domain.GetData(ReadyCallbacksKey) is List<Action>))
                domain.SetData(ReadyCallbacksKey, new List<Action>());
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            Steamworks.SteamInventory.OnDefinitionsUpdated += OnDefinitionsUpdated;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            Steamworks.SteamInventory.OnDefinitionsUpdated -= OnDefinitionsUpdated;
            _subscribed = false;
        }

        private void OnDefinitionsUpdated()
        {
            TryInitialize();
        }

        private void EnsureRunner()
        {
            if (_runnerObject != null)
                return;

            _runnerObject = new GameObject("PlayerDLCAPI_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runnerObject);
            _runnerObject.AddComponent<InitializationRunner>().Begin(this);
        }

        internal bool TryInitialize()
        {
            if (_initialized)
                return true;

            if (ItemManager.itemList == null || ItemManager.itemList.Count == 0)
                return false;

            Steamworks.InventoryDef[] definitions = Steamworks.SteamInventory.Definitions;
            if (definitions == null || definitions.Length == 0)
                return false;

            try
            {
                ProcessDefinitions(definitions);
                _initialized = true;
                Debug.Log(
                    "[PlayerDLCAPI] OK: Initialized " +
                    _contentIdToSkin.Count + " content IDs, " +
                    _workshopToContentId.Count + " workshop IDs, and " +
                    _dlcById.Count + " DLC app IDs.");
                NotifyReady();
                return true;
            }
            catch (Exception ex)
            {
                _initialized = false;
                ClearIndexes();
                Debug.LogWarning("[PlayerDLCAPI] FAIL: Initialization will retry: " + ex.Message);
                return false;
            }
        }

        private void ProcessDefinitions(Steamworks.InventoryDef[] definitions)
        {
            ClearIndexes();

            ItemSkinDirectory.Skin[] skins = ItemSkinDirectory.Instance.skins;
            for (int i = 0; i < skins.Length; i++)
            {
                ItemSkinDirectory.Skin skin = skins[i];
                _contentIdToSkin[skin.id] = skin;

                ItemSkin itemSkin = skin.invItem as ItemSkin;
                if (itemSkin == null)
                {
                    ItemDefinition definition = ItemManager.FindItemDefinition(skin.itemid);
                    if (definition != null)
                        MapContent(definition, skin.id);
                    continue;
                }

                if (itemSkin.UnlockedByDefault)
                    continue;

                if (itemSkin.workshopID != 0UL)
                    _workshopToContentId[itemSkin.workshopID] = skin.id;

                ItemDefinition redirect = itemSkin.Redirect;
                if (redirect == null)
                    continue;

                MapContent(redirect, skin.id);

                ItemDefinition baseDefinition = itemSkin.itemDefinition;
                if (baseDefinition == null &&
                    string.Equals(itemSkin.itemname, "lr300.item", StringComparison.Ordinal))
                {
                    baseDefinition = ItemManager.FindItemDefinition("rifle.lr300");
                }

                if (baseDefinition != null)
                    _redirectedShortnameToBaseItem[redirect.shortname] = baseDefinition.shortname;

                _redirectedItemIdToBaseItem[redirect.itemid] = skin.itemid;
                _redirectedShortnameToContentId[redirect.shortname] = skin.id;
                _redirectedIdToContentId[redirect.itemid] = skin.id;
            }

            foreach (ItemDefinition definition in ItemManager.itemList)
            {
                if (definition == null)
                    continue;

                if (definition.steamItem != null)
                    MapContent(definition, definition.steamItem.id);

                SteamDLCItem dlc = definition.steamDlc;
                if (dlc != null && !dlc.bypassLicenseCheck)
                {
                    _dlcById[dlc.dlcAppID] = dlc;
                    _itemIdToDlcId[definition.itemid] = dlc.dlcAppID;
                    _shortnameToDlcId[definition.shortname] = dlc.dlcAppID;
                }

                if (definition.isRedirectOf != null &&
                    !_redirectedItemIdToBaseItem.ContainsKey(definition.itemid))
                {
                    int contentId;
                    if (!SkinHelpers.TryGetRedirectSkinId(definition, out contentId) || contentId == 0)
                        contentId = definition.itemid;

                    _redirectedShortnameToBaseItem[definition.shortname] =
                        definition.isRedirectOf.shortname;
                    _redirectedItemIdToBaseItem[definition.itemid] =
                        definition.isRedirectOf.itemid;
                    _redirectedShortnameToContentId[definition.shortname] = contentId;
                    _redirectedIdToContentId[definition.itemid] = contentId;
                    MapContent(definition, contentId);

                    if (!_contentIdToSkin.ContainsKey(contentId))
                        _contentIdToSkin[contentId] = default(ItemSkinDirectory.Skin);
                }
            }

            ItemDefinition twitchRivalsFlag =
                ItemManager.FindItemDefinition("twitchrivalsflag");
            if (twitchRivalsFlag != null)
            {
                MapContent(twitchRivalsFlag, twitchRivalsFlag.itemid);
                if (!_contentIdToSkin.ContainsKey(twitchRivalsFlag.itemid))
                {
                    _contentIdToSkin[twitchRivalsFlag.itemid] =
                        default(ItemSkinDirectory.Skin);
                }
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                Steamworks.InventoryDef definition = definitions[i];
                ulong workshopId;
                if (!ulong.TryParse(
                        definition.GetProperty(WorkshopIdProperty),
                        out workshopId))
                {
                    if (!_contentIdToSkin.ContainsKey(definition.Id))
                        continue;
                    workshopId = (ulong)definition.Id;
                }

                if (!_contentIdToSkin.ContainsKey(definition.Id))
                {
                    _contentIdToSkin[definition.Id] =
                        default(ItemSkinDirectory.Skin);
                }

                _workshopToContentId[workshopId] = definition.Id;
            }
        }

        private void MapContent(ItemDefinition definition, int contentId)
        {
            if (definition == null)
                return;

            _itemIdToContentId[definition.itemid] = contentId;
            if (!string.IsNullOrEmpty(definition.shortname))
                _shortnameToContentId[definition.shortname] = contentId;
        }

        private void ClearIndexes()
        {
            _contentIdToSkin.Clear();
            _workshopToContentId.Clear();
            _itemIdToContentId.Clear();
            _shortnameToContentId.Clear();
            _dlcById.Clear();
            _itemIdToDlcId.Clear();
            _shortnameToDlcId.Clear();
            _redirectedShortnameToBaseItem.Clear();
            _redirectedItemIdToBaseItem.Clear();
            _redirectedShortnameToContentId.Clear();
            _redirectedIdToContentId.Clear();
        }

        private static void NotifyReady()
        {
            List<Action> callbacks =
                AppDomain.CurrentDomain.GetData(ReadyCallbacksKey) as List<Action>;
            if (callbacks == null || callbacks.Count == 0)
                return;

            Action[] snapshot = callbacks.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i]?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PlayerDLCAPI] Ready callback failed: " + ex.Message);
                }
            }
        }

        public static void RegisterReadyCallback(Action callback)
        {
            if (callback == null)
                return;

            AppDomain domain = AppDomain.CurrentDomain;
            List<Action> callbacks = domain.GetData(ReadyCallbacksKey) as List<Action>;
            if (callbacks == null)
            {
                callbacks = new List<Action>();
                domain.SetData(ReadyCallbacksKey, callbacks);
            }

            if (!callbacks.Contains(callback))
                callbacks.Add(callback);

            if (Initialized())
                callback();
        }

        public static void UnregisterReadyCallback(Action callback)
        {
            List<Action> callbacks =
                AppDomain.CurrentDomain.GetData(ReadyCallbacksKey) as List<Action>;
            callbacks?.Remove(callback);
        }

        public static int GetGeneration()
        {
            object value = AppDomain.CurrentDomain.GetData(GenerationKey);
            return value is int generation ? generation : 0;
        }

        public static bool Initialized()
        {
            return Instance != null && Instance._initialized;
        }

        public static bool IsPaidSkin(ulong workshopId)
        {
            PlayerDlcApiMod api = Instance;
            if (api == null || !api._initialized)
                return false;

            int contentId;
            if (!api.TryResolveContentId(workshopId, out contentId))
                return false;

            return api._contentIdToSkin.ContainsKey(contentId);
        }

        public static bool FilterPaidSkins(List<ulong> workshopIds)
        {
            if (!Initialized() || workshopIds == null)
                return false;

            for (int i = workshopIds.Count - 1; i >= 0; i--)
            {
                if (IsPaidSkin(workshopIds[i]))
                    workshopIds.RemoveAt(i);
            }
            return true;
        }

        public static bool FilterOwnedOrFreeSkins(
            BasePlayer player,
            List<ulong> workshopIds)
        {
            if (!Initialized() || player == null || workshopIds == null)
                return false;

            for (int i = workshopIds.Count - 1; i >= 0; i--)
            {
                if (!IsOwnedOrFreeSkin(player, workshopIds[i]))
                    workshopIds.RemoveAt(i);
            }
            return true;
        }

        public static bool IsOwnedOrFreeSkin(BasePlayer player, ulong workshopId)
        {
            PlayerDlcApiMod api = Instance;
            if (api == null || !api._initialized || player == null)
                return false;

            int contentId;
            if (!api.TryResolveContentId(workshopId, out contentId))
                return true;

            ItemSkinDirectory.Skin skin;
            if (!api._contentIdToSkin.TryGetValue(contentId, out skin))
                return true;

            return api.CheckSkinContentOwnership(player, contentId, skin);
        }

        public static bool IsDLCItem(Item item)
        {
            return item != null && IsDLCItem(item.info.itemid, item.skin);
        }

        public static bool IsDLCItem(int itemId, ulong skin = 0UL)
        {
            PlayerDlcApiMod api = Instance;
            if (api == null || !api._initialized)
                return false;

            return api._itemIdToContentId.ContainsKey(itemId) ||
                   api._itemIdToDlcId.ContainsKey(itemId) ||
                   (skin != 0UL && IsPaidSkin(skin));
        }

        public static bool IsDLCItem(string shortname, ulong skin = 0UL)
        {
            PlayerDlcApiMod api = Instance;
            if (api == null || !api._initialized || string.IsNullOrEmpty(shortname))
                return false;

            return api._shortnameToContentId.ContainsKey(shortname) ||
                   api._shortnameToDlcId.ContainsKey(shortname) ||
                   (skin != 0UL && IsPaidSkin(skin));
        }

        public static bool IsOwnedOrFreeItem(BasePlayer player, Item item)
        {
            return item != null &&
                   IsOwnedOrFreeItem(player, item.info.itemid, item.skin);
        }

        public static bool IsOwnedOrFreeItem(
            BasePlayer player,
            int itemId,
            ulong skin = 0UL)
        {
            PlayerDlcApiMod api = Instance;
            if (api == null || !api._initialized || player == null)
                return false;

            int contentId;
            if (api._itemIdToContentId.TryGetValue(itemId, out contentId) &&
                !CheckContentOwnership(player, contentId))
                return false;

            int dlcAppId;
            if (api._itemIdToDlcId.TryGetValue(itemId, out dlcAppId) &&
                !api.PlayerOwnsDlc(player, dlcAppId))
                return false;

            return skin == 0UL || IsOwnedOrFreeSkin(player, skin);
        }

        public static bool IsOwnedOrFreeItem(
            BasePlayer player,
            string shortname,
            ulong skin = 0UL)
        {
            PlayerDlcApiMod api = Instance;
            if (api == null || !api._initialized || player == null ||
                string.IsNullOrEmpty(shortname))
                return false;

            int contentId;
            if (api._shortnameToContentId.TryGetValue(shortname, out contentId) &&
                !CheckContentOwnership(player, contentId))
                return false;

            int dlcAppId;
            if (api._shortnameToDlcId.TryGetValue(shortname, out dlcAppId) &&
                !api.PlayerOwnsDlc(player, dlcAppId))
                return false;

            return skin == 0UL || IsOwnedOrFreeSkin(player, skin);
        }

        public static bool FilterOwnedOrFreeItems(
            BasePlayer player,
            List<Item> items)
        {
            if (!Initialized() || player == null || items == null)
                return false;

            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsOwnedOrFreeItem(player, items[i]))
                    items.RemoveAt(i);
            }
            return true;
        }

        public static bool FilterContentOwnership(
            BasePlayer player,
            List<int> contentOrDlcAppIds)
        {
            if (!Initialized() || player == null || contentOrDlcAppIds == null)
                return false;

            for (int i = contentOrDlcAppIds.Count - 1; i >= 0; i--)
            {
                if (!CheckContentOwnership(player, contentOrDlcAppIds[i]))
                    contentOrDlcAppIds.RemoveAt(i);
            }
            return true;
        }

        public static bool CheckContentOwnership(
            BasePlayer player,
            int contentOrDlcAppId)
        {
            PlayerDlcApiMod api = Instance;
            if (api == null || !api._initialized || player == null)
                return false;

            if (api._dlcById.ContainsKey(contentOrDlcAppId))
                return api.PlayerOwnsDlc(player, contentOrDlcAppId);

            ItemSkinDirectory.Skin skin;
            if (!api._contentIdToSkin.TryGetValue(contentOrDlcAppId, out skin))
                return true;

            return api.CheckSkinContentOwnership(
                player,
                contentOrDlcAppId,
                skin);
        }

        // Redirect maps only contain data after indexing completes; do not gate
        // these reads on Initialized() so empty-map behavior matches Oxide.
        public static bool IsRedirectedSkin(string shortname)
        {
            PlayerDlcApiMod api = Instance;
            return api != null &&
                   !string.IsNullOrEmpty(shortname) &&
                   api._redirectedShortnameToBaseItem.ContainsKey(shortname);
        }

        public static bool IsRedirectedSkin(int itemId)
        {
            PlayerDlcApiMod api = Instance;
            return api != null &&
                   api._redirectedItemIdToBaseItem.ContainsKey(itemId);
        }

        public static string GetRedirectedShortname(string shortname)
        {
            PlayerDlcApiMod api = Instance;
            string result;
            return api != null &&
                   !string.IsNullOrEmpty(shortname) &&
                   api._redirectedShortnameToBaseItem.TryGetValue(
                       shortname,
                       out result)
                ? result
                : shortname;
        }

        public static int GetRedirectedItemId(int itemId)
        {
            PlayerDlcApiMod api = Instance;
            int result;
            return api != null &&
                   api._redirectedItemIdToBaseItem.TryGetValue(itemId, out result)
                ? result
                : itemId;
        }

        public static string GetRedirectedShortnameIfNotOwned(
            BasePlayer player,
            string shortname)
        {
            PlayerDlcApiMod api = Instance;
            if (api == null || !api._initialized || player == null)
                return shortname;

            int contentId;
            if (!api._redirectedShortnameToContentId.TryGetValue(
                    shortname,
                    out contentId) ||
                contentId == 0 ||
                CheckContentOwnership(player, contentId))
                return shortname;

            return GetRedirectedShortname(shortname);
        }

        public static int GetRedirectedItemIdIfNotOwned(
            BasePlayer player,
            int itemId)
        {
            PlayerDlcApiMod api = Instance;
            if (api == null || !api._initialized || player == null)
                return itemId;

            int contentId;
            if (!api._redirectedIdToContentId.TryGetValue(itemId, out contentId) ||
                contentId == 0 ||
                CheckContentOwnership(player, contentId))
                return itemId;

            return GetRedirectedItemId(itemId);
        }

        private bool TryResolveContentId(ulong workshopId, out int contentId)
        {
            if (_workshopToContentId.TryGetValue(workshopId, out contentId) &&
                contentId != 0)
                return true;

            if (workshopId > int.MaxValue)
            {
                contentId = 0;
                return false;
            }

            contentId = (int)workshopId;
            return true;
        }

        private bool CheckSkinContentOwnership(
            BasePlayer player,
            int contentId,
            ItemSkinDirectory.Skin skin)
        {
            SteamInventoryItem inventoryItem = skin.invItem;
            if (skin.id != 0 && inventoryItem != null &&
                inventoryItem.HasUnlocked(player))
                return true;

            return player.blueprints != null &&
                   player.blueprints.steamInventory != null &&
                   player.blueprints.steamInventory.HasItem(contentId);
        }

        private bool PlayerOwnsDlc(BasePlayer player, int dlcAppId)
        {
            SteamDLCItem dlc;
            return _dlcById.TryGetValue(dlcAppId, out dlc) &&
                   dlc != null &&
                   dlc.HasLicense(player);
        }

        private sealed class InitializationRunner : MonoBehaviour
        {
            private PlayerDlcApiMod _mod;

            public void Begin(PlayerDlcApiMod mod)
            {
                _mod = mod;
                StartCoroutine(WaitForDefinitions());
            }

            private IEnumerator WaitForDefinitions()
            {
                float elapsed = 0f;
                while (_mod != null && !_mod.TryInitialize())
                {
                    yield return new WaitForSeconds(1f);
                    elapsed += 1f;
                    if (elapsed >= 10f)
                    {
                        elapsed = 0f;
                        Debug.LogWarning(
                            "[PlayerDLCAPI] Waiting for Rust item and Steam inventory definitions.");
                    }
                }
            }
        }
    }
}
