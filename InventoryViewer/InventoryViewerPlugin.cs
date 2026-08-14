using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Rust;
using UnityEngine;
using UnityEngine.Networking;

namespace InventoryViewer
{
    public class InventoryViewerPlugin
    {
        private const string PermUse = "inventoryviewer.allowed";
        private const string PermUnlock = "inventoryviewer.unlock";
        private static readonly string CoffinPrefab = "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab";

        private readonly string _configPath;
        private readonly string _langPath;
        private readonly Dictionary<string, string> _lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, LootingData> _viewingtarget = new Dictionary<ulong, LootingData>();
        private readonly Dictionary<LootableCorpse, List<Item>> _logtaken = new Dictionary<LootableCorpse, List<Item>>();
        private readonly Dictionary<LootableCorpse, List<Item>> _loggiven = new Dictionary<LootableCorpse, List<Item>>();
        private Configuration _config;

        public InventoryViewerPlugin(string serverRoot)
        {
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "InventoryViewer.json");
            _langPath = Path.Combine(serverRoot, "HarmonyLanguage", "InventoryViewer.json");
        }

        public void Load()
        {
            LoadDefaultMessages();
            LoadLangFile();
            LoadConfig();
            RegisterPermissions();
        }

        public void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(PermUse);
            PermissionsBridge.RegisterPermission(PermUnlock);
        }

        public void Unload()
        {
            foreach (var a in _viewingtarget.Values)
            {
                if (a.corpse != null)
                    a.corpse.Kill();
                if (a.backpack != null)
                    a.backpack.Kill();
            }
            _viewingtarget.Clear();
        }

        public void ViewInvCmd(BasePlayer player, string[] args)
        {
            if (!PermissionsBridge.UserHasPermission(player.UserIDString, PermUse))
            {
                player.ChatMessage(Lang("NoPerms", player.UserIDString));
                return;
            }

            if (args == null || args.Length == 0 || string.IsNullOrEmpty(args[0]))
            {
                RaycastHit hitinfo;
                if (!Physics.Raycast(player.eyes.HeadRay(), out hitinfo, 3f, (int)Layers.Server.Players))
                {
                    player.ChatMessage(Lang("NoPlayersFoundRayCast", player.UserIDString));
                    return;
                }
                BasePlayer targetplayerhit = hitinfo.GetEntity()?.ToPlayer();
                if (targetplayerhit == null)
                {
                    player.ChatMessage(Lang("NoPlayersFoundRayCast", player.UserIDString));
                    return;
                }
                player.ChatMessage(Lang("ViewingPLayer", player.UserIDString, targetplayerhit.displayName));
                ViewInventory(player, targetplayerhit);
                return;
            }

            BasePlayer targetplayer = FindPlayer(args[0]);
            if (targetplayer == null)
            {
                player.ChatMessage(Lang("NoPlayersFound", player.UserIDString, args[0]));
                return;
            }
            player.ChatMessage(Lang("ViewingPLayer", player.UserIDString, targetplayer.displayName));
            ViewInventory(player, targetplayer);
        }

        public void _ViewInventory(BasePlayer basePlayer, BasePlayer targetPlayer)
        {
            if (!PermissionsBridge.UserHasPermission(basePlayer.UserIDString, PermUse)) return;
            ViewInventory(basePlayer, targetPlayer);
        }

        private void ViewInventory(BasePlayer player, BasePlayer targetplayer)
        {
            player.EndLooting();
            LootableCorpse corpse = GameManager.server.CreateEntity(StringPool.Get(2604534927), Vector3.zero) as LootableCorpse;
            if (_config.timeout != 0)
            {
                var capturedPlayer = player;
                var capturedCorpse = corpse;
                InventoryViewerMod.Instance?.Delay(() => EndCorpseLooting(capturedPlayer, capturedCorpse), _config.timeout);
            }

            corpse.syncPosition = false;
            corpse.limitNetworking = true;
            corpse.playerName = $"{targetplayer.displayName} - ({targetplayer.userID})";
            corpse.playerSteamID = 0;
            corpse.enableSaving = false;
            corpse.Spawn();
            corpse.CancelInvoke(corpse.RemoveCorpse);
            corpse.SetFlag(BaseEntity.Flags.Locked, true);
            Buoyancy buoyancy;
            if (corpse.TryGetComponent(out buoyancy))
                UnityEngine.Object.Destroy(buoyancy);
            Rigidbody rigidbody;
            if (corpse.TryGetComponent(out rigidbody))
                UnityEngine.Object.Destroy(rigidbody);
            corpse.SendAsSnapshot(player.Connection);

            InventoryViewerMod.Instance?.Delay(() => StartLooting(player, targetplayer, corpse), 0.3f);
        }

        private void StartLooting(BasePlayer player, BasePlayer targetplayer, LootableCorpse corpse)
        {
            if (player == null || targetplayer == null || corpse == null || corpse.IsDestroyed) return;
            player.inventory.loot.AddContainer(targetplayer.inventory.containerMain);
            player.inventory.loot.AddContainer(targetplayer.inventory.containerWear);
            player.inventory.loot.AddContainer(targetplayer.inventory.containerBelt);
            player.inventory.loot.entitySource = corpse;
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.MarkDirty();
            player.inventory.loot.SendImmediate();
            player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "player_corpse");

            LootingData lootingData;
            if (_viewingtarget.TryGetValue(GetUserId(player), out lootingData))
            {
                if (lootingData.targetPlayer != targetplayer)
                {
                    if (lootingData.corpse != null)
                        lootingData.corpse.Kill();
                    if (lootingData.backpack != null)
                        lootingData.backpack.Kill();
                }
                _viewingtarget[GetUserId(player)] = new LootingData { corpse = corpse, targetPlayer = targetplayer, backpack = null };
            }
            else
                _viewingtarget.Add(GetUserId(player), new LootingData { corpse = corpse, targetPlayer = targetplayer });

            if (_config.consolelogging)
                Debug.LogWarning($"[InventoryViewer] {player.displayName}({player.userID}) is viewing the inventory of {targetplayer.displayName}({targetplayer.userID})");
        }

        public void OnLootEntityEnd(BasePlayer player, StorageContainer container)
        {
            LootingData lootingData;
            if (!_viewingtarget.TryGetValue(GetUserId(player), out lootingData))
                return;

            if (lootingData.backpack != null)
            {
                lootingData.backpack.Kill();
                lootingData.backpack = null;
                InventoryViewerMod.Instance?.Delay(() =>
                {
                    if (player != null && lootingData.targetPlayer != null && lootingData.corpse != null)
                        StartLooting(player, lootingData.targetPlayer, lootingData.corpse);
                }, 0.3f);
                return;
            }

            if (lootingData.corpse != null)
            {
                if (_config.discordlogging)
                    player.StartCoroutine(LogToDiscord(player, lootingData.targetPlayer, lootingData.corpse));
                lootingData.corpse.Kill();
                _viewingtarget.Remove(GetUserId(player));
            }
        }

        private void EndCorpseLooting(BasePlayer player, LootableCorpse corpse)
        {
            if (corpse == null || corpse.IsDestroyed) return;
            LootingData lootingData;
            if (!_viewingtarget.TryGetValue(GetUserId(player), out lootingData)) return;
            if (_config.discordlogging)
                player.StartCoroutine(LogToDiscord(player, lootingData.targetPlayer, corpse));
            _viewingtarget.Remove(GetUserId(player));
            if (corpse != null && !corpse.IsDestroyed)
                corpse.Kill();
        }

        private class LootingData
        {
            public LootableCorpse corpse;
            public StorageContainer backpack;
            public BasePlayer targetPlayer;
        }

        public object CanMoveItem(Item item, PlayerInventory playerInventory, ItemContainerId targetContainer, int targetSlot, int amount)
        {
            BasePlayer player = playerInventory.baseEntity;
            if (player == null) return null;
            LootingData lootingData;
            if (!_viewingtarget.TryGetValue(GetUserId(player), out lootingData))
                return null;

            if (lootingData.backpack == null)
            {
                if (item.IsBackpack() && item.contents != null && item.contents.itemList.Count > 0)
                {
                    ViewBackpack(player, item);
                    return false;
                }
            }

            LootableCorpse corpse = lootingData.corpse;
            if (corpse != null && corpse.HasFlag(BaseEntity.Flags.Locked) && !PermissionsBridge.UserHasPermission(player.UserIDString, PermUnlock))
                return false;

            if (_config.discordlogging && corpse != null)
            {
                if (targetContainer.Value == 0)
                {
                    AddLog(_logtaken, corpse, item);
                    return null;
                }
                ItemContainer targetcon = player.inventory.FindContainer(targetContainer);
                if (targetcon != null && targetcon.GetOwnerPlayer() == player && item.parent != null && item.parent.playerOwner != player)
                {
                    AddLog(_logtaken, corpse, item);
                    Item targetitem = targetcon.GetSlot(targetSlot);
                    if (targetitem != null)
                        AddLog(_loggiven, corpse, targetitem);
                }
                else if (item.parent != null && item.parent.playerOwner == player)
                {
                    AddLog(_loggiven, corpse, item);
                    if (targetcon != null)
                    {
                        Item targetitem = targetcon.GetSlot(targetSlot);
                        if (targetitem != null)
                            AddLog(_logtaken, corpse, targetitem);
                    }
                }
            }
            return null;
        }

        private static void AddLog(Dictionary<LootableCorpse, List<Item>> dict, LootableCorpse corpse, Item item)
        {
            List<Item> list;
            if (dict.TryGetValue(corpse, out list))
                list.Add(item);
            else
                dict[corpse] = new List<Item> { item };
        }

        private void ViewBackpack(BasePlayer player, Item targetItem)
        {
            LootingData lootingData;
            if (!_viewingtarget.TryGetValue(GetUserId(player), out lootingData)) return;
            StorageContainer storage = GameManager.server.CreateEntity(CoffinPrefab, Vector3.zero) as StorageContainer;
            storage.syncPosition = false;
            storage.limitNetworking = true;
            storage.enableSaving = false;
            storage.Spawn();
            storage.inventory.playerOwner = player;
            DestroyOnGroundMissing groundMissing;
            if (storage.TryGetComponent(out groundMissing))
                UnityEngine.Object.Destroy(groundMissing);
            GroundWatch groundWatch;
            if (storage.TryGetComponent(out groundWatch))
                UnityEngine.Object.Destroy(groundWatch);
            lootingData.backpack = storage;

            InventoryViewerMod.Instance?.Delay(() =>
            {
                if (player == null || targetItem?.contents == null || storage == null || storage.IsDestroyed) return;
                player.inventory.loot.Clear();
                player.inventory.loot.AddContainer(targetItem.contents);
                player.inventory.loot.entitySource = storage;
                player.inventory.loot.PositionChecks = false;
                player.inventory.loot.MarkDirty();
                player.inventory.loot.SendImmediate();
                player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "generic_resizable");
                if (_config.consolelogging)
                    Debug.LogWarning($"[InventoryViewer] {player.displayName} viewing backpack");
            }, 0.1f);
        }

        private static BasePlayer FindPlayer(string nameOrId)
        {
            BasePlayer exact = null;
            int matches = 0;
            var list = BasePlayer.activePlayerList;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || !p.IsConnected) continue;
                if (p.UserIDString == nameOrId)
                    return p;
                if (p.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    exact = p;
                    matches++;
                }
            }
            return matches == 1 ? exact : null;
        }

        private class Configuration
        {
            [JsonProperty("View inventory raycast distance")]
            public float raycastdist = 10;
            [JsonProperty("View inventory timeout (seconds) set to 0 to disable")]
            public float timeout = 60;
            [JsonProperty("Use console logging")]
            public bool consolelogging = false;
            [JsonProperty("Use discord logging")]
            public bool discordlogging = false;
            [JsonProperty("Webhook URL")]
            public string discordwebhook = "";
            [JsonProperty("Discord name")]
            public string discordname = "Inventory Viewer";
            [JsonProperty("Discord avatar URL")]
            public string discordavatarurl = "https://i.imgur.com/BLoVcpz.png";
            [JsonProperty("View Backpack Button AnchorMin")]
            public string ImageAnchorMin = "0.175 0.017";
            [JsonProperty("View Backpack Button AnchorMax")]
            public string ImageAnchorMax = "0.22 0.08";
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                    _config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(_configPath));
            }
            catch (Exception ex) { Debug.LogWarning("[InventoryViewer] Config: " + ex.Message); }
            _config ??= new Configuration();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
            catch { }
        }

        private string Lang(string key, string id = null, params object[] args)
        {
            string msg;
            if (!_lang.TryGetValue(key, out msg) || msg == null) msg = key;
            if (args == null || args.Length == 0) return msg;
            try { return string.Format(msg, args); }
            catch { return msg; }
        }

        private void LoadDefaultMessages()
        {
            void Add(string k, string v) { if (!_lang.ContainsKey(k)) _lang[k] = v; }
            Add("NoPerms", "You don't have permissions to use this command");
            Add("NoPlayersFound", "No players were found by the identifier of {0}");
            Add("NoPlayersFoundRayCast", "No players were found");
            Add("ViewingPLayer", "Viewing <color=orange>{0}'s</color> inventory");
        }

        private void LoadLangFile()
        {
            if (!File.Exists(_langPath)) return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(_langPath));
                if (loaded == null) return;
                foreach (var kv in loaded)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _lang[kv.Key] = kv.Value;
                }
            }
            catch { }
        }

        private static ulong GetUserId(BasePlayer player)
        {
            try { return player.userID.Get(); }
            catch { return (ulong)player.userID; }
        }

        private IEnumerator LogToDiscord(BasePlayer viewer, BasePlayer viewing, LootableCorpse corpse)
        {
            if (string.IsNullOrEmpty(_config.discordwebhook)) yield break;
            var msg = DiscordMessage(viewer, viewing, corpse);
            string jsonmsg = JsonConvert.SerializeObject(msg);
            using (UnityWebRequest wwwpost = new UnityWebRequest(_config.discordwebhook, "POST"))
            {
                byte[] jsonToSend = Encoding.UTF8.GetBytes(jsonmsg);
                wwwpost.uploadHandler = new UploadHandlerRaw(jsonToSend);
                wwwpost.downloadHandler = new DownloadHandlerBuffer();
                wwwpost.SetRequestHeader("Content-Type", "application/json");
                yield return wwwpost.SendWebRequest();
                if (wwwpost.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning("[InventoryViewer] Discord: " + wwwpost.error);
            }
        }

        private Message DiscordMessage(BasePlayer viewer, BasePlayer viewing, LootableCorpse corpse)
        {
            var fields = new List<Message.Fields>
            {
                new Message.Fields("Viewer: ", $"{viewer.displayName}({viewer.userID})", true),
                new Message.Fields("Viewing: ", $"{viewing.displayName}({viewing.userID})", true),
            };
            List<Item> givenlist;
            if (_loggiven.TryGetValue(corpse, out givenlist))
            {
                var given = new StringBuilder();
                for (int i = 0; i < givenlist.Count; i++)
                    given.Append(givenlist[i].amount).Append(" x ").Append(givenlist[i].info.name).Append(", ");
                fields.Add(new Message.Fields("Items given: ", given.ToString(), false));
                _loggiven.Remove(corpse);
            }
            List<Item> takenlist;
            if (_logtaken.TryGetValue(corpse, out takenlist))
            {
                var taken = new StringBuilder();
                for (int i = 0; i < takenlist.Count; i++)
                    taken.Append(takenlist[i].amount).Append(" x ").Append(takenlist[i].info.name).Append(", ");
                fields.Add(new Message.Fields("Items taken: ", taken.ToString(), false));
                _logtaken.Remove(corpse);
            }
            var footer = new Message.Footer($"Logged @{DateTime.UtcNow:dd/MM/yy HH:mm:ss}");
            var embeds = new List<Message.Embeds>
            {
                new Message.Embeds("Server - " + ConVar.Server.hostname, "Inventory viewer log", fields, footer)
            };
            return new Message(_config.discordname, _config.discordavatarurl, embeds);
        }

        public class Message
        {
            public string username { get; set; }
            public string avatar_url { get; set; }
            public List<Embeds> embeds { get; set; }
            public class Fields
            {
                public string name { get; set; }
                public string value { get; set; }
                public bool inline { get; set; }
                public Fields(string name, string value, bool inline)
                {
                    this.name = name;
                    this.value = value;
                    this.inline = inline;
                }
            }
            public class Footer
            {
                public string text { get; set; }
                public Footer(string text) { this.text = text; }
            }
            public class Embeds
            {
                public string title { get; set; }
                public string description { get; set; }
                public List<Fields> fields { get; set; }
                public Footer footer { get; set; }
                public Embeds(string title, string description, List<Fields> fields, Footer footer)
                {
                    this.title = title;
                    this.description = description;
                    this.fields = fields;
                    this.footer = footer;
                }
            }
            public Message(string username, string avatar_url, List<Embeds> embeds)
            {
                this.username = username;
                this.avatar_url = avatar_url;
                this.embeds = embeds;
            }
        }
    }
}
