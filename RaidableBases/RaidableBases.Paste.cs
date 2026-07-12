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

        #region Paste

        private float isSpawnerBusyTime;
        private bool isSpawnerBusy;

        private bool IsLoaderBusy => Raids.Exists(raid => raid.IsDespawning || raid.IsLoading);

        private bool IsSpawnerBusy
        {
            get
            {
                if (Time.time > isSpawnerBusyTime)
                {
                    isSpawnerBusy = false;
                }

                return IsUnloading || isSpawnerBusy;
            }
            set
            {
                isSpawnerBusyTime = Time.time + 180f;
                isSpawnerBusy = value;
            }
        }

        private bool IsGridLoading() => GridController.gridCoroutine != null;

        private bool IsGridBroken() => GridController.gridCoroutine != null && GridController.gridCoroutine.Current == null;

        private bool IsPasteAvailable() => !Raids.Exists(raid => raid.IsLoading);

        private bool IsBusy() => IsSpawnerBusy || IsLoaderBusy || IsGridLoading();

        private Payment TryBuyRaidServerRewards(int cost, BasePlayer buyer, BasePlayer player)
        {
            int points = Convert.ToInt32(ServerRewards?.Call("CheckPoints", buyer.userid()));
            if (points > 0 && points - cost >= 0)
            {
                return new(this, buyer, player, null, cost);
            }

            Message(buyer, "ServerRewardPointsFailed", cost);
            return null;
        }

        private Payment TryBuyRaidEconomics(double cost, BasePlayer buyer, BasePlayer player)
        {
            object obj;
            if ((obj = Economics?.Call("Balance", buyer.userid())) != null || (obj = IQEconomic?.Call("API_GET_BALANCE", buyer.userid())) != null || (obj = BankSystem?.Call("Balance", buyer.userid())) != null)
            {
                var balance = Convert.ToDouble(obj);

                if (balance > 0 && balance - cost >= 0)
                {
                    return new(this, buyer, player, null, 0, cost);
                }
            }

            Message(buyer, "EconomicsWithdrawFailed", cost);
            return null;
        }

        private Payment TryBuyRaidCustom(List<CustomCostOptions> options, BasePlayer buyer, BasePlayer player)
        {
            foreach (var option in options)
            {
                if (option.isPlugin)
                {
                    object plugin = plugins.Find(option.Plugin.PluginName);

                    if (plugin != null)
                    {
                        double balance = 0;

                        if (!string.IsNullOrWhiteSpace(option.Plugin.ShoppyStockShopName))
                        {
                            balance = Convert.ToDouble(plugin?.Call(option.Plugin.BalanceHookName, option.Plugin.ShoppyStockShopName, option.Plugin.PlayerDataType switch
                            {
                                2 => buyer,
                                1 => buyer.UserIDString,
                                0 or _ => buyer.userid()
                            }));
                        }
                        else balance = Convert.ToDouble(plugin?.Call(option.Plugin.BalanceHookName, option.Plugin.PlayerDataType switch
                        {
                            2 => buyer,
                            1 => buyer.UserIDString,
                            0 or _ => buyer.userid()
                        }));

                        if (balance < option.Plugin.Amount)
                        {
                            Message(buyer, "CustomWithdrawFailed", $"{option.GetCurrencyName()} ({option.Plugin.Amount})");
                            return null;
                        }
                    }
                }

                if (!option.isItem)
                {
                    continue;
                }

                using var slots = DisposableList<Item>();
                buyer.inventory.FindItemsByItemID(slots, option.Definition.itemid);
                int amount = 0;

                foreach (var slot in slots)
                {
                    if (option.Skin != 0 && slot.skin != option.Skin)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(option.Name) && slot.name != option.Name && option.Skin == 0)
                    {
                        continue;
                    }

                    amount += slot.amount;

                    if (amount >= option.Amount)
                    {
                        break;
                    }
                }

                if (amount < option.Amount && config.Settings.ShoppyStock != null)
                {
                    amount += Convert.ToInt32(ShoppyStock?.Call("GetCurrencyAmount", config.Settings.ShoppyStock.ShopName, player.userid()));
                }

                if (amount < option.Amount)
                {
                    Message(buyer, "CustomWithdrawFailed", $"{(string.IsNullOrWhiteSpace(option.Name) ? option.Shortname : option.Name)} ({option.Amount})");
                    return null;
                }
            }

            return new(this, buyer, player, options);
        }

        public class Payments
        {
            public Payment Custom;
            public Payment Economics;
            public Payment ServerRewards;
            public BasePlayer owner;
            public bool admin;
            public ulong userid;
            public string username;
            public Vector3 position;
            public int type;
            public bool valid => Payment.IsValid(Custom) || Payment.IsValid(Economics) || Payment.IsValid(ServerRewards);
            public Payments() { }
            public Payments(BasePlayer owner)
            {
                this.owner = owner;
                admin = owner.IsAdmin;
                userid = owner.userID;
                username = owner.displayName;
                position = owner.transform.position;
            }
            public void Refund()
            {
                Custom?.RefundItems();
                Economics?.RefundMoney();
                ServerRewards?.RefundPoints();
            }
            public void Take(bool reset)
            {
                Custom?.TakeItems(reset);
                Economics?.TakeMoney(reset);
                ServerRewards?.TakePoints(reset);
            }
        }

        public class Payment
        {
            public Payment(RaidableBases instance, BasePlayer buyer, BasePlayer owner = null, List<CustomCostOptions> options = null, int RP = 0, double money = 0)
            {
                this.userId = owner?.userID ?? buyer?.userID ?? 0;
                this.buyerName = buyer?.displayName ?? owner?.displayName;
                this.buyerId = buyer?.userID ?? userId;
                this.money = money;
                this.RP = RP;

                Options = options;
                Instance = instance;

                free = money == 0.0 && RP == 0 && options.IsNullOrEmpty();
                paid = free;
            }

            public RaidableBases Instance;
            public bool paid;
            public bool free;
            public int RP;
            public double money;
            public string buyerName;
            public ulong buyerId;
            public ulong userId;
            public BasePlayer _buyer;
            public BasePlayer _owner;
            public BasePlayer buyer { get { if (_buyer == null) { _buyer = RustCore.FindPlayerById(buyerId); } return _buyer; } }
            public BasePlayer owner { get { if (_owner == null) { _owner = RustCore.FindPlayerById(userId); } return _owner; } }
            public List<CustomCostOptions> Options;
            public bool self => buyerId == userId;
            public Configuration config => Instance.config;
            public static bool IsValid(Payment payment) => payment != null && payment.owner != null && payment.buyer != null;
            private void QueueNotification(BasePlayer player, string key, params object[] args) => Instance.Message(player, key, args);

            private void Message(BasePlayer player, string key, params object[] args) => Instance.Message(player, key, args);

            private string mx(string key, string id = null, params object[] args) => Instance.mx(key, id, args);

            public void RefundItems(double percent = 100.0)
            {
                if (!paid) return;
                var target = buyer ?? owner;
                if (target == null) return;

                using var _sb = DisposableBuilder.Get();
                foreach (var option in Options)
                {
                    if (option.isPlugin)
                    {
                        object plugin = Instance.plugins.Find(option.Plugin.PluginName);

                        if (plugin != null)
                        {
                            double amount = Math.Ceiling(option.Plugin.Amount * percent / 100.0);
                            if (amount > 0)
                            {
                                if (!string.IsNullOrWhiteSpace(option.Plugin.ShoppyStockShopName))
                                {
                                    plugin?.Call(option.Plugin.DepositHookName, option.Plugin.ShoppyStockShopName, option.Plugin.PlayerDataType switch
                                    {
                                        2 => target,
                                        1 => target.UserIDString,
                                        0 or _ => target.userid()
                                    }, option.Plugin.AmountDataType switch
                                    {
                                        2 => (object)(int)amount,
                                        1 => (object)(float)amount,
                                        0 or _ => (object)(double)amount
                                    });
                                }
                                else plugin?.Call(option.Plugin.DepositHookName, option.Plugin.PlayerDataType switch
                                {
                                    2 => target,
                                    1 => target.UserIDString,
                                    0 or _ => target.userid()
                                }, option.Plugin.AmountDataType switch
                                {
                                    2 => (object)(int)amount,
                                    1 => (object)(float)amount,
                                    0 or _ => (object)(double)amount
                                });

                                string currencyName = !string.IsNullOrWhiteSpace(option.Plugin.CurrencyName) ? option.Plugin.CurrencyName : string.IsNullOrWhiteSpace(option.Name) ? plugin.GetPluginName() : option.Name;
                                _sb.Append(mx("Refunded Item", target.UserIDString, amount, currencyName)).Append(", ");
                            }
                        }
                    }

                    if (option.isItem)
                    {
                        int amount = (int)Math.Ceiling(option.Amount * percent / 100.0);

                        if (amount > 0)
                        {
                            Item item = ItemManager.CreateByItemID(option.Definition.itemid, amount, option.Skin);

                            _sb.Append(mx("Refunded Item", target.UserIDString, amount, string.IsNullOrWhiteSpace(option.Name) ? item.info.displayName.english : item.name = option.Name)).Append(", ");

                            target.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                        }
                    }
                }

                if (_sb.Length > 2)
                {
                    _sb.Length -= 2;

                    Message(target, _sb.ToString());
                }

                paid = false;
            }

            public void TakeItems(bool reset)
            {
                if (buyer == null)
                {
                    return;
                }

                var sb = new StringBuilder();

                foreach (var option in Options)
                {
                    if (option.isPlugin)
                    {
                        object plugin = Instance.plugins.Find(option.Plugin.PluginName);

                        if (plugin != null)
                        {
                            if (!string.IsNullOrWhiteSpace(option.Plugin.ShoppyStockShopName))
                            {
                                plugin?.Call(option.Plugin.WithdrawHookName, option.Plugin.ShoppyStockShopName, option.Plugin.PlayerDataType switch
                                {
                                    2 => buyer,
                                    1 => buyer.UserIDString,
                                    0 or _ => buyer.userid()
                                }, option.Plugin.AmountDataType switch
                                {
                                    2 => (object)(int)option.Plugin.Amount,
                                    1 => (object)(float)option.Plugin.Amount,
                                    0 or _ => (object)(double)option.Plugin.Amount
                                });

                                paid = true;
                                sb.Append(mx("CustomDepositFormat", userId.ToString(), option.Plugin.Amount, option.GetCurrencyName())).Append(", ");
                            }
                            else
                            {
                                plugin?.Call(option.Plugin.WithdrawHookName, option.Plugin.PlayerDataType switch
                                {
                                    2 => buyer,
                                    1 => buyer.UserIDString,
                                    0 or _ => buyer.userid()
                                }, option.Plugin.AmountDataType switch
                                {
                                    2 => (object)(int)option.Plugin.Amount,
                                    1 => (object)(float)option.Plugin.Amount,
                                    0 or _ => (object)(double)option.Plugin.Amount
                                });

                                paid = true;
                                sb.Append(mx("CustomDepositFormat", userId.ToString(), option.Amount, option.GetCurrencyName())).Append(", ");
                            }
                        }
                    }

                    if (!option.isItem)
                    {
                        continue;
                    }

                    using var slots = DisposableList<Item>();
                    buyer.inventory.FindItemsByItemID(slots, option.Definition.itemid);
                    var amountLeft = option.Amount;

                    foreach (var slot in slots)
                    {
                        if (slot == null || option.Skin != 0 && slot.skin != option.Skin)
                        {
                            continue;
                        }

                        var taken = slot.amount > amountLeft ? slot.SplitItem(amountLeft) : slot;

                        taken.Drop(Vector3.zero, Vector3.zero);

                        amountLeft -= taken.amount;

                        if (amountLeft <= 0)
                        {
                            string name = string.IsNullOrWhiteSpace(option.Name) ? slot.info.displayName.english : option.Name;
                            sb.Append(string.Format("{0} {1}", option.Amount, name)).Append(", ");
                            paid = true;
                            break;
                        }
                    }

                    if (amountLeft > 0 && Instance.ShoppyStock != null && config.Settings.ShoppyStock != null && config.Settings.ShoppyStock.IsItem(option))
                    {
                        Instance.ShoppyStock?.Call("TakeCurrency", config.Settings.ShoppyStock.ShopName, buyer.userid(), amountLeft);
                        CuiHelper.DestroyUi(buyer, $"PopUpAPI_{config.Settings.ShoppyStock.PanelName}_Parent");
                        paid = true;
                    }
                }

                if (sb.Length > 2)
                {
                    sb.Length -= 2;

                    if (!self)
                    {
                        Message(owner, "CustomWithdrawGift", buyerName, sb.ToString());
                    }

                    Message(buyer, reset ? "CustomWithdrawReset" : "CustomWithdraw", sb.ToString());
                }
            }

            public void TakeMoney(bool reset)
            {
                if (money > 0)
                {
                    if (Convert.ToBoolean(Instance.Economics?.Call("Withdraw", userId, money)))
                    {
                        paid = true;
                    }

                    if (Convert.ToBoolean(Instance.BankSystem?.Call("Withdraw", userId, (int)money)))
                    {
                        paid = true;
                    }

                    if (Instance.IQEconomic != null)
                    {
                        Instance.IQEconomic?.Call("API_REMOVE_BALANCE", userId, (int)money);
                        paid = true;
                    }

                    if (!self)
                    {
                        Message(owner, "EconomicsWithdrawGift", buyerName, money);
                    }

                    Message(buyer, reset ? "EconomicsWithdrawReset" : "EconomicsWithdraw", money);
                }
            }

            public void RefundMoney()
            {
                if (paid && money > 0)
                {
                    Instance.BankSystem?.Call("Deposit", userId, (int)money);
                    Instance.Economics?.Call("Deposit", userId, money);
                    Instance.IQEconomic?.Call("API_SET_BALANCE", userId, (int)money);
                    QueueNotification(buyer, "Refunded Money", money);
                    money = 0;
                }
            }

            public void TakePoints(bool reset)
            {
                if (RP > 0)
                {
                    if (Convert.ToBoolean(Instance.ServerRewards?.Call("TakePoints", userId, RP)))
                    {
                        paid = true;
                    }

                    if (!self)
                    {
                        Message(owner, "ServerRewardPointsGift", buyerName, RP);
                    }

                    Message(buyer, reset ? "ServerRewardPointsTakenReset" : "ServerRewardPointsTaken", RP);
                }
            }

            public void RefundPoints()
            {
                if (paid && RP > 0)
                {
                    Instance.ServerRewards?.Call("AddPoints", userId, RP);
                    QueueNotification(buyer, "Refunded RP", RP);
                    RP = 0;
                }
            }
        }

        private bool BuyRaid(string mode, Payments payments, BasePlayer owner, string baseName, bool free)
        {
            if (SpawnRandomBase(RaidableType.Purchased, mode, baseName, owner != null && owner.IsAdmin, payments, owner, null, free))
            {
                Message(owner, "BaseQueued", Queues.queue.Count);
                return true;
            }
            return false;
        }

        private bool IsDifficultyAvailable(string mode, RaidableType type, bool checkAllowPVP)
        {
            foreach (var profile in Buildings.Profiles.Values)
            {
                if (profile.Options.Mode != mode) continue;
                if (!checkAllowPVP || !profile.Options.AllowPVP || config.Settings.Buyable.ConvertPVP || BuyPVP(type)) return CanSpawnDifficultyToday(type, mode);
            }
            return false;
        }

        private bool BuyPVP(RaidableType type)
        {
            if (AllowBuyingPVP || !Buildings.Profiles.All(x => x.Value.Options.AllowPVP)) return true;
            if (type == RaidableType.Maintained && config.Settings.Maintained.ConvertPVP) return true;
            if (type == RaidableType.Scheduled && config.Settings.Schedule.ConvertPVP) return true;
            if (type == RaidableType.Purchased && config.Settings.Buyable.ConvertPVP) return true;
            return type == RaidableType.Manual;
        }

        private bool PasteBuilding(RandomBase rb)
        {
            Queues.Messages.Print($"{rb.BaseName} trying to paste at {rb.Position}");

            if (!IsCopyPasteLoaded(out var error))
            {
                Puts(error);

                return false;
            }

            loadCoroutines.Add(ServerMgr.Instance.StartCoroutine(LoadCopyPasteFile(rb)));

            return true;
        }

        internal void StopLoadCoroutines()
        {
            if (setupCopyPasteObstructionRadius != null)
            {
                ServerMgr.Instance.StopCoroutine(setupCopyPasteObstructionRadius);
                setupCopyPasteObstructionRadius = null;
            }
            if (checkPlayersNearEventsCo != null)
            {
                ServerMgr.Instance.StopCoroutine(checkPlayersNearEventsCo);
                checkPlayersNearEventsCo = null;
            }
            foreach (var co in loadCoroutines)
            {
                if (co != null)
                {
                    ServerMgr.Instance.StopCoroutine(co);
                }
            }
            foreach (var raid in Raids)
            {
                raid.StopSetupCoroutine();
            }
            Queues?.StopCoroutine();
            Automated?.DestroyMe();
            GridController.StopCoroutine();
        }

        private bool IsPrefabFoundation(Dictionary<string, object> entity)
        {
            var prefabname = entity["prefabname"].ToString();

            return prefabname.Contains("/foundation.") || prefabname.EndsWith("diesel_collectable.prefab") && entity.TryGetValue("skinid", out var skinid) && skinid != null && skinid.ToString() == "1337424001";
        }

        private bool IsPrefabExternalWall(Dictionary<string, object> entity)
        {
            return entity["prefabname"].ToString().Contains("/wall.external.high.");
        }

        private bool IsPrefabFloor(Dictionary<string, object> entity)
        {
            return entity.TryGetValue("prefabname", out var obj) && obj != null && obj.ToString().Contains("/floor");
        }

        private IEnumerator SetupCopyPasteObstructionRadius()
        {
            foreach (var profile in Buildings.Profiles.ToPooledList())
            {
                var radius = profile.Value.Options.ProtectionRadii.Obstruction == -1 ? 0f : GetObstructionRadius(profile.Value.Options.ProtectionRadii, RaidableType.None);
                foreach (var extra in profile.Value.Options.AdditionalBases)
                {
                    if (!Buildings.Removed.Contains(extra.Key))
                    {
                        yield return SetupCopyPasteObstructionRadius(extra.Key, radius);
                    }
                }
                if (!Buildings.Removed.Contains(profile.Key))
                {
                    yield return SetupCopyPasteObstructionRadius(profile.Key, radius);
                }
            }

            setupCopyPasteObstructionRadius = null;
        }

        private IEnumerator SetupCopyPasteObstructionRadius(string baseName, float radius)
        {
            var filename = Path.Combine("copypaste", baseName);
            if (!HarmonyDataLayer.ExistsDatafile(filename))
            {
                yield break;
            }

            HarmonyDataFile data;
            try
            {
                data = HarmonyDataLayer.GetDatafile(filename);
            }
            catch (Exception ex)
            {
                Queues.Messages.Log(baseName, $"{baseName} could not be read from the disk #1: {ex}");
                Buildings.Remove(baseName);
                yield break;
            }

            if (data["entities"] == null)
            {
                Queues.Messages.Log(baseName, $"{baseName} is missing entity data");
                Buildings.Remove(baseName);
                yield break;
            }

            var entities = data["entities"] as List<object>;
            using var foundations = DisposableList<Vector3>();
            using var floors = DisposableList<Vector3>();
            //using var invalid = DisposableList<string>();
            int checks = 0;
            float x = 0f;
            float z = 0f;

            foreach (var obj in entities)
            {
                if (!(obj is Dictionary<string, object> entity))
                {
                    continue;
                }
                if (++checks >= 1000)
                {
                    checks = 0;
                    yield return Automated.instruction0;
                }
                if (!entity.ContainsKey("prefabname") || !entity.ContainsKey("pos"))
                {
                    continue;
                }
                var prefab = entity["prefabname"].ToString();
                try
                {
                    if (prefab.Contains("testridablehorse"))
                    {
                        Puts($"{baseName} contains a broken prefab that must be removed: {prefab}");
                        Queues.Messages.Log(baseName, $"Invalid entity! {prefab}");
                        Buildings.Remove(baseName);
                        //invalid.Add(prefab);
                        yield break;
                    }
                    var axes = entity["pos"] as Dictionary<string, object>;
                    var position = new Vector3(Convert.ToSingle(axes?["x"]), Convert.ToSingle(axes?["y"]), Convert.ToSingle(axes?["z"]));
                    if (IsPrefabFoundation(entity) || IsPrefabExternalWall(entity))
                    {
                        foundations.Add(position);
                        x += position.x;
                        z += position.z;
                    }
                    if (IsPrefabFloor(entity))
                    {
                        floors.Add(position);
                    }
                }
                catch (Exception ex)
                {
                    Puts(ex);
                    Puts("Invalid entity found in copypaste file: {0} ({1})", baseName, prefab);
                }
            }

            if (foundations.Count == 0)
            {
                foreach (var position in floors)
                {
                    foundations.Add(position);
                    x += position.x;
                    z += position.z;
                }
            }

            if (foundations.Count == 0)
            {
                Queues.Messages.Log(baseName, $"{baseName} is missing foundation/floor data #1");
                Buildings.Remove(baseName);
                yield break;
            }

            var center = new Vector3(x / foundations.Count, 0f, z / foundations.Count);

            center.y = GetSpawnHeight(center);

            if (radius == 0f)
            {
                foundations.Sort((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));

                radius = Vector3.Distance(foundations[0], foundations[^1]);
            }

            var pasteData = GetPasteData(baseName);

            pasteData.radius = Mathf.Ceil(Mathf.Max(CELL_SIZE, radius));
            pasteData.foundations = new(foundations);
            pasteData.valid = true;

            //if (invalid.Count > 0)
            //{
            //    pasteData.invalid = new(invalid);
            //}
        }

        private readonly Dictionary<string, object> _emptyProtocol = new();

        private IEnumerator LoadCopyPasteFile(RandomBase rb)
        {
            HarmonyDataFile data;

            try
            {
                data = HarmonyDataLayer.GetDatafile(Path.Combine("copypaste", rb.BaseName));
            }
            catch (Exception ex)
            {
                Queues.Messages.Log(rb.BaseName, $"{rb.BaseName} could not be read from the disk #2: {ex}");
                Buildings.Remove(rb.BaseName);
                IsSpawnerBusy = false;
                yield break;
            }

            yield return ApplyStartPositionAdjustment(rb, data);

            if (rb.pasteData.foundations.IsNullOrEmpty())
            {
                Queues.Messages.Log(rb.BaseName, $"{rb.BaseName} is missing foundation/floor data #2");
                Buildings.Remove(rb.BaseName);
                IsSpawnerBusy = false;
                yield break;
            }

            var entities = data["entities"] as List<object>;

            if (entities == null)
            {
                Queues.Messages.Log(rb.BaseName, $"{rb.BaseName} is missing entity data");
                Buildings.Remove(rb.BaseName);
                IsSpawnerBusy = false;
                yield break;
            }

            //if (!rb.pasteData.invalid.IsNullOrEmpty())
            //{
            //    foreach (var invalid in rb.pasteData.invalid)
            //    {
            //        foreach (var ent in entities)
            //        {
            //            if (ent is Dictionary<string, object> dict && dict.TryGetValue("prefabname", out object value) && value.ToString() == invalid)
            //            {
            //                entities.Remove(dict);
            //                break;
            //            }
            //        }
            //    }
            //}

            var preloadData = CopyPasteAPI.Call("PreLoadData", entities, rb.Position, 0f, true, rb.inventories, false, true) as ICollection<Dictionary<string, object>>;

            yield return TryApplyAutoHeight(rb, preloadData);

            if (!IsUnloading)
            {
                TryInvokeMethod(() => RFManager.GetListenerSet(1).RemoveWhere(obj => obj == null || !BaseEntityEx.IsValidEntityReference(obj)));

                var raid = OpenEvent(rb);

                if (raid.SpawnLegacyShelter())
                {
                    CreatePastedCallback(raid, rb)();
                    yield break;
                }

                var protocol = data["protocol"] as Dictionary<string, object> ?? _emptyProtocol;
                object result = null;
                try
                {
                    result = CopyPasteAPI.Call("Paste", new object[] { preloadData, protocol, false, rb.Position, _consolePlayer, rb.stability, 0f, rb.heightAdj, false, CreatePastedCallback(raid, rb), CreateSpawnCallback(raid), rb.BaseName, true, rb.Save });
                }
                catch (Exception ex)
                {
                    Puts(ex);
                }
                if (result == null)
                {
                    Queues.Messages.Print($"CopyPaste {CopyPasteAPI.Version} did not respond for {rb.BaseName}!");
                    Puts($"\nCopyPaste {CopyPasteAPI.Version} did not respond for {rb.BaseName}! Is CopyPaste Harmony mod loaded?");
                    Puts("\nQueue will resume in 180 seconds to prevent the server from being spammed with errors.");
                    rb.payments.Refund();
                    raid.Despawn();
                }
                else
                {
                    Queues.Messages.Print($"{rb.BaseName} is pasting at {rb.Position}");
                }
            }
        }

        private Action CreatePastedCallback(RaidableBase raid, RandomBase rb)
        {
            return new(() =>
            {
                raid.IsPasted = true;

                if (raid.IsUnloading)
                {
                    rb.payments.Refund();
                    raid.rb = rb;
                    raid.Despawn();
                }
                else
                {
                    raid.Init(rb);
                }
            });
        }

        private Action<BaseEntity> CreateSpawnCallback(RaidableBase raid)
        {
            return new(e =>
            {
                if (IsUnloading || e == null || e.IsDestroyed)
                {
                    return;
                }
                if (e is BaseCombatEntity b)
                {
                    b.spawnDeployableCorpseOnDeath = false;
                }
                if (e.ShortPrefabName == "poweredwaterpurifier.storage" && !e.HasParent())
                {
                    e.DelayedSafeKill();
                    return;
                }
                Vector3 position = e.transform.position;
                if (e is AutoTurret turret)
                {
                    raid.PreSetupTurret(turret);
                }
                else if (raid.IsWeapon(e))
                {
                    e.skinID = RB_SKIN_ID;
                }
                else if (e is BaseMountable && !e.HasParent())
                {
                    e.skinID = RB_SKIN_ID;
                }
                else if (raid.IsFoundation(e))
                {
                    raid.foundations.Add(position);
                }
                else if (e.ShortPrefabName.Contains("floor"))
                {
                    raid.floors.Add(position);
                }
                if (!raid.stability && e is BuildingBlock block)
                {
                    block.grounded = true;
                }
                else if (raid.Options.EmptyAll && e is StorageContainer container)
                {
                    raid.TryEmptyContainer(container);
                }
                else if (raid.Options.EmptyAll && e is IOEntity io)
                {
                    raid.TryEmptyIndustrialStorage(io);
                }
                foreach (var slot in _checkSlots)
                {
                    if (e.GetSlot(slot) is BaseEntity ent)
                    {
                        raid.AddEntity(ent);
                    }
                }
                if (e.net == null)
                {
                    e.net = Net.sv.CreateNetworkable();
                }
                if (Mathf.Abs(position.y - raid.Location.y) < raid.ProtectionRadius && raid.IsCompound(e))
                {
                    raid.compound.Add(position);
                }
                if (e.children != null)
                {
                    foreach (var child in e.children)
                    {
                        if (child != null && (child.enableSaving || child is HeldEntity))
                            continue;
                        BaseEntity.saveList.Remove(child);
                    }
                }
                if (!raid.Options.Elevators.BMGOnly && e is Elevator elevator)
                {
                    raid.SetupElevator(elevator);
                }
                e.OwnerID = 0;
                e.EnableSaving(false);
                raid.AddEntity(e);
            });
        }

        private IEnumerator ApplyStartPositionAdjustment(RandomBase rb, HarmonyDataFile data)
        {
            ParseListedOptions(rb);

            using var foundations = DisposableList<Vector3>();
            float x = 0f, z = 0f;

            if (!rb.pasteData.valid)
            {
                yield return SetupCopyPasteObstructionRadius(rb.BaseName, rb.options.ProtectionRadii.Obstruction == -1 ? 0f : GetObstructionRadius(rb.options.ProtectionRadii, RaidableType.None));
            }

            if (rb.pasteData.foundations.IsNullOrEmpty())
            {
                Queues.Messages.Log(rb.BaseName, $"{rb.BaseName} is missing foundation/floor data #3");
                yield break;
            }

            foreach (var foundation in rb.pasteData.foundations)
            {
                var a = foundation + rb.Position;
                a.y = GetSpawnHeight(a);
                foundations.Add(a);
                x += a.x;
                z += a.z;
            }

            var center = new Vector3(x / foundations.Count, 0f, z / foundations.Count);

            center.y = rb.isCustomSpawn ? rb.Position.y : GetSpawnHeight(center, !rb.options.Water.IsWaterSpawn);
            
            rb.Position += (rb.Position - center);

            if (rb.options.Setup.ForcedHeight == -1)
            {
                if (rb.options.Water.IsWaterSpawn && rb.options.Water.Surface)
                {
                    rb.Position.y = Mathf.Max(0f, TerrainMeta.WaterMap.GetHeight(rb.Position));
                }
                else if (!rb.isCustomSpawn) rb.Position.y = GetSpawnHeight(rb.Position, !rb.options.Water.IsWaterSpawn);

                TryApplyCustomAutoHeight(rb);
                TryApplyMultiFoundationSupport(rb);

                rb.Position.y += rb.baseHeight + rb.options.Setup.PasteHeightAdjustment;
            }
            else rb.Position.y = rb.baseHeight + rb.options.Setup.PasteHeightAdjustment + rb.options.Setup.ForcedHeight;

            yield return CoroutineEx.waitForFixedUpdate;
        }

        private IEnumerator TryApplyAutoHeight(RandomBase rb, ICollection<Dictionary<string, object>> preloadData)
        {
            if (rb.autoHeight && !config.Settings.Experimental.Contains(ExperimentalSettings.Type.AutoHeight, rb))
            {
                var bestHeight = Convert.ToSingle(CopyPasteAPI.Call("FindBestHeight", preloadData, rb.Position));
                int checks = 0;

                rb.heightAdj = bestHeight - rb.Position.y;

                foreach (var entity in preloadData)
                {
                    if (++checks >= 1000)
                    {
                        checks = 0;
                        yield return Automated.instruction0;
                    }

                    if (entity.TryGetValue("position", out var obj) && obj is Vector3 pos)
                    {
                        pos.y += rb.heightAdj;

                        entity["position"] = pos;
                    }
                }
            }
        }

        private void TryApplyCustomAutoHeight(RandomBase rb)
        {
            if (config.Settings.Experimental.Contains(ExperimentalSettings.Type.AutoHeight, rb))
            {
                foreach (var foundation in rb.pasteData.foundations)
                {
                    var a = foundation + rb.Position;

                    if (a.y < rb.Position.y)
                    {
                        rb.Position.y += rb.Position.y - a.y;
                        return;
                    }
                    else
                    {
                        rb.Position.y -= a.y - rb.Position.y;
                        return;
                    }
                }
            }
        }

        private void TryApplyMultiFoundationSupport(RandomBase rb)
        {
            float j = 0f, k = 0f, y = 0f;
            for (int i = 0; i < rb.pasteData.foundations.Count; i++)
            {
                y = (float)Math.Round(rb.pasteData.foundations[i].y, 1);
                j = Mathf.Max(y, j);
                k = Mathf.Min(y, k);
            }
            if (j != 0f && config.Settings.Experimental.Contains(ExperimentalSettings.Type.MultiFoundation, rb))
            {
                rb.Position.y += j + 1f;
            }
            else if (k != 0f && config.Settings.Experimental.Contains(ExperimentalSettings.Type.Bunker, rb))
            {
                y = rb.Position.y + Mathf.Abs(k);
                if (y < rb.Position.y)
                {
                    rb.Position.y = y + 1.4f;
                }
            }
        }

        [HookMethod("GetSpawnHeight")]
        public float GetSpawnHeight(Vector3 a, bool flag = true, bool shouldSkipSmallRock = false) => SpawnsController.GetSpawnHeight(a, flag, shouldSkipSmallRock);

        private void ParseListedOptions(RandomBase rb)
        {
            rb.autoHeight = false;

            List<PasteOption> options = rb.options.PasteOptions;

            foreach (var (key, abo) in rb.options.AdditionalBases)
            {
                if (key.Equals(rb.BaseName, StringComparison.OrdinalIgnoreCase))
                {
                    options = abo.Options;
                    break;
                }
            }

            foreach (var option in options)
            {
                switch (option.Key.ToLower())
                {
                    case "inventories": rb.inventories = option.Value.ToLower() == "true"; break;
                    case "stability": rb.stability = option.Value.ToLower() == "true"; break;
                    case "autoheight": rb.autoHeight = option.Value.ToLower() == "true"; break;
                    case "height" when float.TryParse(option.Value, out var y): rb.baseHeight = y; break;
                }
            }
        }

        private object SpawnRandomBase(string b = null, Vector3 a = default, int m = -1, int t = 1, bool free = true)
        {
            var type = (RaidableType)t;
            var mode = GetRaidableMode(m.ToString());
            var (key, profile) = GetBuilding(type, mode, b, null);
            
            if (!IsProfileValid(key, profile, free, RaidableType.Manual))
            {
                return "API_INVALID_PROFILE";
            }

            var spawns = GetSpawns(type, profile, out var checkTerrain);

            if (a == Vector3.zero)
            {
                return SpawnRandomBase(type, mode, key);
            }

            return AddSpawnToQueue(key, profile, checkTerrain, type, spawns, null, null, null, a);
        }

        private bool SpawnRandomBase(RaidableType type, string mode, string baseName = null, bool isAdmin = false, Payments payments = null, BasePlayer owner = null, IPlayer user = null, bool free = false)
        {
            var (key, profile) = GetBuilding(type, mode, baseName, owner);
            var validProfile = IsProfileValid(key, profile, free, type);
            var spawns = GetSpawns(type, profile, out var checkTerrain);
            var blockedPurchasePVP = BlockedPurchasePVP;
            BlockedPurchasePVP = false;

            if (validProfile && spawns != null)
            {
                return AddSpawnToQueue(key, profile, checkTerrain, type, spawns, payments, owner, user, Vector3.zero);
            }
            else if (type == RaidableType.Maintained || type == RaidableType.Scheduled)
            {
                Queues.Messages.PrintAll();
            }
            else Queues.Messages.Add(GetDebugMessage(mode, type, validProfile, isAdmin, owner?.UserIDString, baseName, profile?.Options), null);

            if (!validProfile)
            {
                if (payments != null)
                {
                    if (!string.IsNullOrWhiteSpace(baseName) && profile != null && !profile.Options.Enabled)
                    {
                        Message(owner, "Profile Not Enabled", baseName);
                    }
                    else
                    {
                        Message(owner, "Difficulty Not Buyable", mode);
                        if (blockedPurchasePVP && owner != null && owner.IsAdmin) Message(owner, "'Allow Players To Buy PVP Raids' is preventing you from buying this PVP raid. Set 'Allow PVP' to 'false' in the PROFILE to fix this.");
                    }
                    payments.Refund();
                }
                else if (user != null)
                {
                    user.Message(Queues.Messages.GetLast());
                }
            }

            return false;
        }

        private bool AddSpawnToQueue(string key, BaseProfile profile, bool checkTerrain, RaidableType type, RaidableSpawns spawns, Payments payments = null, BasePlayer owner = null, IPlayer user = null, Vector3 point = default)
        {
            RandomBase rb = new();

            rb.Instance = this;
            rb.BaseName = key;
            rb.Profile = profile;
            rb.Position = point;
            rb.type = type;
            rb.spawns = spawns ??= new(this);
            rb.payments = payments ??= new();
            rb.pasteData = GetPasteData(key);
            rb.checkTerrain = checkTerrain;
            rb.owner = owner;
            rb.user = user;
            rb.id = owner?.UserIDString ?? "";
            rb.userid = owner?.userID ?? 0;
            rb.username = owner?.displayName ?? "";
            rb.typeDistance = GetDistance(rb.type);
            rb.protectionRadius = rb.options.ProtectionRadius(rb.type);
            rb.safeRadius = Mathf.Max(rb.options.ArenaWalls.Radius, rb.protectionRadius);
            rb.buildRadius = Mathf.Max(config.Settings.Management.CupboardDetectionRadius, rb.options.ArenaWalls.Radius, rb.protectionRadius) + 5f;

            if (!rb.payments.admin && owner != null)
            {
                rb.payments.admin = owner.IsAdmin;
            }

            if (rb.buildRadius < 105f && !rb.spawns.IsCustomSpawn)
            {
                rb.buildRadius = 105f;
            }

            Queues.Add(rb);

            return true;
        }

        private string GetDebugMessage(string mode, RaidableType type, bool validProfile, bool isAdmin, string id, string baseName, BuildingOptions options)
        {
            if (options != null)
            {
                if (!options.Enabled)
                {
                    return mx("Profile Not Enabled", id, baseName);
                }
                else if (options.Mode == RaidableMode.Disabled)
                {
                    return mx("Difficulty Disabled", id, baseName);
                }
            }

            if (!validProfile)
            {
                return Queues.Messages.GetLast(id);
            }

            if (!string.IsNullOrWhiteSpace(baseName))
            {
                if (!FileExists(baseName))
                {
                    return mx("FileDoesNotExist", id);
                }
                else if (!Buildings.IsConfigured(baseName))
                {
                    return mx("BuildingNotConfigured", id);
                }
            }

            if (!IsDifficultyAvailable(mode, type, options?.AllowPVP ?? false) && mode != RaidableMode.Random)
            {
                return mx(isAdmin ? "Difficulty Not Available Admin" : "Difficulty Not Available", id, mode);
            }
            else if (Buildings.Profiles.Count == 0)
            {
                return mx("NoBuildingsConfigured", id);
            }

            return Queues.Messages.GetLast(id);
        }

        public RaidableSpawns GetSpawns(RaidableType type, BaseProfile profile, out bool checkTerrain)
        {
            checkTerrain = false;
            RaidableSpawns spawns;
            return profile != null && profile.Spawns.TryGetValue(type, out var s) && s.IsCustomSpawn ? s : type switch
            {
                RaidableType.Maintained when GridController.Spawns.TryGetValue(RaidableType.Maintained, out spawns) => spawns,
                RaidableType.Manual when GridController.Spawns.TryGetValue(RaidableType.Manual, out spawns) => spawns,
                RaidableType.Purchased when GridController.Spawns.TryGetValue(RaidableType.Purchased, out spawns) => spawns,
                RaidableType.Scheduled when GridController.Spawns.TryGetValue(RaidableType.Scheduled, out spawns) => spawns,
                _ => GridController.Spawns.TryGetValue(RaidableType.Grid, out spawns) && (checkTerrain = true) ? spawns : null
            };
        }

        private bool BlockedPurchasePVP;
        public (string, BaseProfile) GetBuilding(RaidableType type, string mode, string baseName, BasePlayer player = null)
        {
            if (!string.IsNullOrWhiteSpace(baseName) && Buildings.Removed.Contains(baseName))
            {
                return default;
            }

            bool isBaseNull = string.IsNullOrWhiteSpace(baseName) || baseName.Length == 1 && baseName[0] >= '0' && baseName[0] <= '4';
            using var profiles = DisposableList<(string, BaseProfile)>();

            foreach (var (key, profile) in Buildings.Profiles)
            {
                if (MustExclude(type, profile.Options.AllowPVP))
                {
                    Queues.Messages.Add($"{type} is not configured to include {(profile.Options.AllowPVP ? "PVP" : "PVE")} bases.");
                    continue;
                }

                if (!IsBuildingAllowed(type, mode, profile.Options))
                {
                    if (type == RaidableType.Purchased && !AllowBuyingPVP && profile.Options.AllowPVP && !isBaseNull && profile.Options.AdditionalBases.ContainsKey(baseName))
                    {
                        BlockedPurchasePVP = true;
                    }
                    continue;
                }

                if (!profile.Options.Permission.Has(player, type))
                {
                    continue;
                }

                if (FileExists(key) && (key == baseName || data.Cycle.CanSpawn(type, mode, key, player)))
                {
                    if (!profile.Options.Enabled && key != baseName)
                    {
                        continue;
                    }

                    if (isBaseNull)
                    {
                        profiles.Add((key, profile));
                    }
                    else if (key.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        return (key, profile);
                    }
                }

                foreach (var (extra, abo) in profile.Options.AdditionalBases)
                {
                    if (FileExists(extra) && (extra == baseName || data.Cycle.CanSpawn(type, mode, extra, player)))
                    {
                        if (!profile.Options.Enabled && extra != baseName)
                        {
                            continue;
                        }

                        var clone = BaseProfile.Clone(profile, extra);

                        clone.Options.PasteOptions = abo.Options.ToList();
                        clone.ProfileName = extra;

                        if (isBaseNull)
                        {
                            profiles.Add((extra, clone));
                        }
                        else if (extra.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                        {
                            return (extra, clone);
                        }
                    }
                }
            }

            if (profiles.Count > 0)
            {
                return profiles.GetRandom();
            }

            if (type == RaidableType.Purchased && !AllowBuyingPVP && Buildings.Profiles.All(x => x.Value.Options.Mode == mode && x.Value.Options.AllowPVP))
            {
                Queues.Messages.Print($"Your config has Allow Players To Buy PVP Raids disabled, and your {mode} profile has Allow PVP enabled which is blocking all purchases of this difficulty.");
            }
            else if (!AnyCopyPasteFileExists)
            {
                Queues.Messages.Print("No copypaste file in any profile exists");
            }
            else Queues.Messages.Print($"Building is unavailable", $"{mode} {type}");

            return default;
        }

        private bool IsProfileValid(string key, BaseProfile profile, bool free, RaidableType type)
        {
            if (string.IsNullOrWhiteSpace(key) || profile == null || profile.Options == null)
            {
                return false;
            }

            return free || profile.Options.Mode != RaidableMode.Disabled && profile.Options.Enabled || !profile.Options.Enabled && type == RaidableType.Manual;
        }

        public string GetRandomDifficulty(RaidableType type)
        {
            using var modes = DisposableList<string>();

            foreach (var mode in GetRaidableModes())
            {
                if (!CanSpawnDifficultyToday(type, mode))
                {
                    Queues.Messages.Add("Cannot spawn difficulty today", mode);
                    continue;
                }

                int maxAllowed = config.Settings.Management.Amounts.Get(this, type, mode);

                if (maxAllowed < 0 || (maxAllowed > 0 && Get(mode, false) >= maxAllowed))
                {
                    Queues.Messages.Add("Max amount of events reached for difficulty", mode);
                    continue;
                }

                foreach (var profile in Buildings.Profiles.Values)
                {
                    if (profile.Options.Mode == mode && !MustExclude(type, profile.Options.AllowPVP))
                    {
                        modes.Add(mode);
                        break;
                    }
                }
            }

            if (modes.Count > 0)
            {
                return config.Settings.Management.Chances.SelectRandomMode(this, modes);
            }

            Queues.Messages.Add("Nothing left to spawn.");

            return RaidableMode.Random;
        }

        private bool DataFileExists(string file)
        {
            return HarmonyDataLayer.ExistsDatafile(file);
        }

        private bool FileExists(string file)
        {
            return HarmonyDataLayer.ExistsDatafile(Path.Combine("copypaste", file));
        }

        private bool IsBuildingAllowed(RaidableType type, string search, BuildingOptions options) => (search == RaidableMode.Random || search == options.Mode) && type switch
        {
            _ when !IsDifficultyEnabledAfterWipe(options.Mode, type, string.Empty, out _) => (Queues.Messages.Add("Cannot spawn difficulty yet", options.Mode), false).Item2,
            RaidableType.Purchased when !CanSpawnDifficultyToday(type, options.Mode) => (Queues.Messages.Add("Cannot spawn difficulty today", options.Mode), false).Item2,
            RaidableType.Purchased when !AllowBuyingPVP && options.AllowPVP => (Queues.Messages.Add("Buyable Events is configured to block PVP purchases.", options.Mode), false).Item2,
            RaidableType.Maintained or RaidableType.Scheduled when !CanSpawnDifficultyToday(type, options.Mode) => (Queues.Messages.Add("Cannot spawn difficulty today", options.Mode), false).Item2,
            _ => true
        };

        private bool isDifficultyEnabledAfterWipeOverridden;

        private bool IsDifficultyEnabledAfterWipe(string mode, RaidableType type, string userid, out double remainingHours)
        {
            double requiredHours = isDifficultyEnabledAfterWipeOverridden ? 0 : type switch
            {
                RaidableType.Purchased => config.Settings.Buyable.Wipe.Get(userid, mode),
                RaidableType.Maintained => config.Settings.Maintained.Wipe.Get(mode),
                RaidableType.Scheduled => config.Settings.Schedule.Wipe.Get(mode),
                _ => 0
            };
            if (requiredHours > 0)
            {
                double elapsedHours = (DateTime.UtcNow - SaveRestore.SaveCreatedTime).TotalHours;
                remainingHours = requiredHours - elapsedHours;
                return mode == RaidableMode.Legacy || elapsedHours >= requiredHours;
            }
            remainingHours = 0;
            return true;
        }

        private bool CanSpawnDifficultyToday(RaidableType type, string mode) => !config.Settings.Management.Dictionary.TryGetValue(en ? $"{mode} Raids Can Spawn On" : $"Дни спавна {mode} рейд-баз", out var value) || !config.Settings.Buyable.UseCanSpawnOnOptions && type == RaidableType.Purchased || GetDifficultyDay(value);

        private bool GetDifficultyDay(DayLimitSettings ds) => DateTime.Now.DayOfWeek switch { DayOfWeek.Monday => ds.Monday, DayOfWeek.Tuesday => ds.Tuesday, DayOfWeek.Wednesday => ds.Wednesday, DayOfWeek.Thursday => ds.Thursday, DayOfWeek.Friday => ds.Friday, DayOfWeek.Saturday => ds.Saturday, _ => ds.Sunday };

        #endregion

    }
}
