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
using Color = UnityEngine.Color;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Paste

        private double isSpawnerBusyTime;
        private bool isSpawnerBusy;

        private bool IsLoaderBusy(out string str)
        {
            foreach (var raid in Raids)
            {
                if (raid.IsDespawning)
                {
                    str = raid.BaseName + " is despawning";
                    return true;
                }
                if (raid.IsLoading)
                {
                    str = raid.BaseName + " is loading";
                    return true;
                }
            }
            str = null;
            return false;
        }

        private bool IsSpawnerBusy
        {
            get
            {
                if (Time.timeAsDouble > isSpawnerBusyTime)
                {
                    isSpawnerBusy = false;
                }

                return IsUnloading || isSpawnerBusy;
            }
            set
            {
                isSpawnerBusyTime = Time.timeAsDouble + 180d;
                isSpawnerBusy = value;
            }
        }

        private bool IsGridLoading() => GridController.gridCoroutine != null;

        private bool IsGridBroken() => GridController.gridCoroutine != null && GridController.gridCoroutine.Current == null;

        private bool IsPasteAvailable() => !IsLoaderBusy(out _);

        private bool IsBusy() => IsSpawnerBusy || IsLoaderBusy(out _) || IsGridLoading();

        private Payment TryBuyRaidServerRewards(int cost, BasePlayer buyer, BasePlayer player)
        {
            int points = Convert.ToInt32(ServerRewards?.Call("CheckPoints", buyer.userid()));
            if (points > 0 && points - cost >= 0)
            {
                return new(this, buyer, player, null, cost);
            }

            SendNotification(buyer, "ServerRewardPointsFailed", cost);
            return null;
        }

        private Payment TryBuyRaidEconomics(double cost, BasePlayer buyer, BasePlayer player)
        {
            object obj;
            if ((obj = Economics?.Call("Balance", buyer.userid())) != null && Convert.ToDouble(obj) >= cost) return Create(Payment.EconomyProvider.Economics);
            if ((obj = IQEconomic?.Call("API_GET_BALANCE", buyer.userid())) != null && Convert.ToDouble(obj) >= cost) return Create(Payment.EconomyProvider.IQEconomic);
            if ((obj = BankSystem?.Call("Balance", buyer.userid())) != null && Convert.ToDouble(obj) >= cost) return Create(Payment.EconomyProvider.BankSystem);
            SendNotification(buyer, "EconomicsWithdrawFailed", cost);
            return null;
            Payment Create(Payment.EconomyProvider provider)
            {
                Payment payment = new(this, buyer, player, null, 0, cost);
                payment.provider = provider;
                return payment;
            }
        }

        private Payment TryBuyRaidCustom(List<CustomCostOptions> options, BasePlayer buyer, BasePlayer player)
        {
            foreach (var option in options)
            {
                if (option.isPlugin)
                {
                    object plugin = plugins.Find(option.Plugin.PluginName);
                    double balance = 0;

                    if (plugin != null)
                    {
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
                    }
                    else
                    {
                        SendNotification(buyer, "PluginNotLoaded", option.Plugin.PluginName);
                    }

                    if (balance < option.Plugin.Amount)
                    {
                        SendNotification(buyer, "CustomWithdrawFailed", $"{option.GetCurrencyName()} ({option.Plugin.Amount})");
                        return null;
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
                    if (slot == null || option.Skin != 0 && slot.skin != option.Skin)
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
                    amount += Convert.ToInt32(ShoppyStock?.Call("GetCurrencyAmount", config.Settings.ShoppyStock.ShopName, buyer.userid()));
                }

                if (amount < option.Amount)
                {
                    SendNotification(buyer, "CustomWithdrawFailed", $"{(string.IsNullOrWhiteSpace(option.Name) ? option.Shortname : option.Name)} ({option.Amount})");
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

            internal enum EconomyProvider { None, Economics, IQEconomic, BankSystem }
            internal EconomyProvider provider;
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
            private void Notify(BasePlayer player, string key, params object[] args) => Instance.SendNotification(player, key, args);

            private string mx(string key, string id = null, params object[] args) => Instance.mx(key, id, args);

            public string RefundItems(double percent = 100.0)
            {
                if (!paid) return null;
                var target = buyer ?? owner;
                if (target == null) return null;

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

                    Notify(target, _sb.ToString());
                }

                paid = false;
                return _sb.ToString();
            }

            public bool TakeItems(bool reset, bool callOnItemUse = false)
            {
                if (free || buyer == null)
                {
                    return false;
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
                                    0 or _ => buyerId
                                }, option.Plugin.AmountDataType switch
                                {
                                    2 => (object)(int)option.Plugin.Amount,
                                    1 => (object)(float)option.Plugin.Amount,
                                    0 or _ => (object)(double)option.Plugin.Amount
                                });

                                paid = true;
                                sb.Append(mx("CustomDepositFormat", userId.ToString(), option.Plugin.Amount, option.GetCurrencyName())).Append(", ");
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
                        if (slot == null || option.Skin != 0 && slot.skin != option.Skin || !string.IsNullOrWhiteSpace(option.Name) && option.Skin == 0 && slot.name != option.Name)
                        {
                            continue;
                        }

                        int amount = Math.Min(slot.amount, amountLeft);
                        amountLeft -= amount;
                        slot.amount -= amount;
                        slot.ReduceItemOwnership(amount);
                        if (slot.amount <= 0) slot.Remove();
                        else slot.MarkDirty();

                        if (amountLeft <= 0)
                        {
                            string name = string.IsNullOrWhiteSpace(option.Name) ? option.Definition.displayName.english : option.Name;
                            sb.Append(string.Format("{0} {1}", option.Amount, name)).Append(", ");
                            paid = true;
                            break;
                        }
                    }

                    if (amountLeft > 0 && Instance.ShoppyStock != null && config.Settings.ShoppyStock != null && config.Settings.ShoppyStock.IsItem(option))
                    {
                        Instance.ShoppyStock?.Call("TakeCurrency", config.Settings.ShoppyStock.ShopName, buyerId, amountLeft);
                        CuiHelper.DestroyUi(buyer, $"PopUpAPI_{config.Settings.ShoppyStock.PanelName}_Parent");
                        paid = true;
                    }
                }

                if (sb.Length > 2)
                {
                    sb.Length -= 2;

                    if (!self)
                    {
                        Notify(owner, "CustomWithdrawGift", buyerName, sb.ToString());
                    }

                    Notify(buyer, reset ? "CustomWithdrawReset" : "CustomWithdraw", sb.ToString());
                }

                return paid;
            }

            public bool TakeMoney(bool reset)
            {
                if (money > 0)
                {
                    switch (provider)
                    {
                        case EconomyProvider.Economics:
                            paid = Convert.ToBoolean(Instance.Economics?.Call("Withdraw", buyerId, money));
                            break;

                        case EconomyProvider.BankSystem:
                            paid = Convert.ToBoolean(Instance.BankSystem?.Call("Withdraw", buyerId, (int)money));
                            break;

                        case EconomyProvider.IQEconomic when Instance.IQEconomic != null:
                            Instance.IQEconomic?.Call("API_REMOVE_BALANCE", buyerId, (int)money);
                            paid = true;
                            break;
                    }

                    if (!paid)
                    {
                        Notify(buyer, "EconomicsWithdrawFailed", money);
                        return false;
                    }

                    if (!self)
                    {
                        Notify(owner, "EconomicsWithdrawGift", buyerName, money);
                    }

                    Notify(buyer, reset ? "EconomicsWithdrawReset" : "EconomicsWithdraw", money);
                }

                return paid;
            }

            public double RefundMoney(double percent = 100.0)
            {
                if (paid && money > 0)
                {
                    double amount = (int)Math.Ceiling(money * percent / 100.0);
                    if (provider == EconomyProvider.BankSystem) Instance.BankSystem?.Call("Deposit", buyerId, (int)amount);
                    if (provider == EconomyProvider.Economics) Instance.Economics?.Call("Deposit", buyerId, amount);
                    if (provider == EconomyProvider.IQEconomic) Instance.IQEconomic?.Call("API_SET_BALANCE", buyerId, (int)amount);
                    if (provider != EconomyProvider.None) Notify(buyer, "Refunded Money", amount);
                    money = 0;
                    return amount;
                }
                return 0;
            }

            public bool TakePoints(bool reset)
            {
                if (RP > 0)
                {
                    if (Convert.ToBoolean(Instance.ServerRewards?.Call("TakePoints", buyerId, RP)))
                    {
                        paid = true;
                    }

                    if (!self)
                    {
                        Notify(owner, "ServerRewardPointsGift", buyerName, RP);
                    }

                    Notify(buyer, reset ? "ServerRewardPointsTakenReset" : "ServerRewardPointsTaken", RP);
                }

                return paid;
            }

            public int RefundPoints(double percent = 100.0)
            {
                if (paid && RP > 0)
                {
                    int amount = (int)Math.Ceiling(RP * percent / 100.0);
                    Instance.ServerRewards?.Call("AddPoints", buyerId, amount);
                    Notify(buyer, "Refunded RP", amount);
                    RP = 0;
                    return amount;
                }
                return 0;
            }
        }

        private bool BuyRaid(string mode, Payments payments, BasePlayer owner, string baseName, bool free)
        {
            if (SpawnRandomBase(RaidableType.Purchased, mode, baseName, owner != null && owner.IsAdmin, payments, owner, null, free))
            {
                SendNotification(owner, "BaseQueued", Queues.queue.Count);
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

        private void OnCopyFinished(List<object> rawData, string filename, IPlayer user, Vector3 sourcePos)
        {
            filename = Path.GetFileNameWithoutExtension(filename);

            if (_pasteData.TryGetValue(filename, out var pasteData))
            {
                pasteData.valid = false;
            }
        }

        public void InvalidatePasteData()
        {
            foreach (var pasteData in _pasteData.Values)
            {
                pasteData.valid = false;
            }
            Buildings.Removed.Clear();
        }

        private bool PasteBuilding(RandomBase rb)
        {
            Queues.Messages.Print($"{rb.BaseName} trying to paste at {rb.Position}");

            if (!IsPasteEngineReady(out var error))
            {
                Puts(error);

                return false;
            }

            loadCoroutines[rb] = ServerMgr.Instance.StartCoroutine(LoadCopyPasteFile(rb));

            return true;
        }

        private IEnumerator PasteManualEvent(RandomBase rb, Vector3 candidate, BasePlayer player, IPlayer user)
        {
            bool pasteStarted = false;

            try
            {
                if (Queues.UsesPrecisePlacement(rb))
                {
                    yield return Queues.TryResolvePrecisePlacement(rb, candidate);
                }

                bool precisePlacement = rb.precisePlacement;

                loadCoroutines.Remove(rb);

                pasteStarted = PasteBuilding(rb);
                if (pasteStarted)
                {
                    if (player != null && player.IsAdmin)
                    {
                        DrawText(player, 10f, precisePlacement ? Color.cyan : Color.red, rb.Position, rb.BaseName);
                    }

                    if (ConVar.Server.hostname.Contains("Test Server"))
                    {
                        DrawSphere(player, 30f, Color.blue, rb.Position, rb.pasteData.radius);
                    }
                }
            }
            finally
            {
                if (!pasteStarted)
                {
                    loadCoroutines.Remove(rb);
                }
            }
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
            using var coroutines = loadCoroutines.Values.ToPooledList();
            loadCoroutines.Clear();
            foreach (var co in coroutines)
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

        private const float FoundationCornerOffset = 1.4f;
        private const float FoundationTriangleTipOffset = 2.8f;
        private const float FoundationContactClearance = 3f;
        private const float FoundationSurfaceClearance = 0.2f;
        private const float FloorContactClearance = 0.8f;
        private const float ImportantEntityBurialAllowance = 0.2f;
        private const float SuggestedBandAboveClearance = 1f;
        private const float StairContactOffset = 1.4f;
        private const float RampContactOffset = 0.85f;
        private const float FloatingCornerMax = 0.25f;
        private const float FloatingCornerAverageMax = 0.125f;
        private const float FloatingCornerPercentMax = 2.5f;
        private const int FloatingCornerGraceCount = 2;
        private const float FloatingFoundationExemptHeight = 6f;
        private const float FoundationLevelPrecision = 10f;
        private const float LiftFalloff = 20f; //FP literal
        private const float VehicleCeilingMargin = 20f; //Margin with full lift above top of base

        // WaterBases stores InternalEntityType in Reserved15-17 and marks it with Reserved18. Reserved2 is not part of this marker.
        private const int WaterBasesBarrelType = 0;
        private const int WaterBasesFoundationSquareType = 2;
        private const int WaterBasesFoundationTriangleType = 3;
        private const string WaterBasesFoundationSquarePrefab = "assets/prefabs/building core/floor/floor.prefab";
        private const string WaterBasesFoundationTrianglePrefab = "assets/prefabs/building core/floor.triangle/floor.triangle.prefab";
        private const string WaterBasesDieselPrefab = "assets/content/structures/excavator/prefabs/diesel_collectable.prefab";

        private readonly string[] ImportantPastePrefabs =
        {
            "cupboard.tool.",
            "wall.doorway",
            "wall.frame.garagedoor",
            "door.double.hinged",
            "autoturret",
            "flameturret.deployed",
            "refinery_small_deployed",
            "fridge.deployed",
            "furnace",
            "bbq.deployed",
            "locker.deployed",
            "shopfront",
            "wall.window",
            "sam_site_turret",
            "weaponrack",
            "fireplace"
        };

        private readonly string[] NpcImportantPastePrefabs =
        {
            "rug.",
            "sleepingbag",
            "bed_deployed",
            "beachtowel"
        };

        private static bool HasPasteFlag(Dictionary<string, object> flags, string name)
        {
            return flags.TryGetValue(name, out var obj) && obj is true;
        }

        private static int GetWaterBasesEntityType(BaseEntity entity)
        {
            if (entity == null || !entity.HasFlag(BaseEntity.Flags.Reserved18))
            {
                return -1;
            }

            int type = 0;
            if (entity.HasFlag(BaseEntity.Flags.Reserved15)) type |= 1;
            if (entity.HasFlag(BaseEntity.Flags.Reserved16)) type |= 2;
            if (entity.HasFlag(BaseEntity.Flags.Reserved17)) type |= 4;
            return type;
        }

        private static int GetWaterBasesEntityType(Dictionary<string, object> entity)
        {
            if (!entity.TryGetValue("flags", out var obj) || obj is not Dictionary<string, object> flags || !HasPasteFlag(flags, nameof(BaseEntity.Flags.Reserved18)))
            {
                return -1;
            }

            int type = 0;
            if (HasPasteFlag(flags, nameof(BaseEntity.Flags.Reserved15))) type |= 1;
            if (HasPasteFlag(flags, nameof(BaseEntity.Flags.Reserved16))) type |= 2;
            if (HasPasteFlag(flags, nameof(BaseEntity.Flags.Reserved17))) type |= 4;
            return type;
        }

        private static bool IsWaterBasesFoundation(BaseEntity entity)
        {
            if (entity is not BuildingBlock)
            {
                return false;
            }

            return entity.ShortPrefabName switch
            {
                "floor" => GetWaterBasesEntityType(entity) == WaterBasesFoundationSquareType,
                "floor.triangle" => GetWaterBasesEntityType(entity) == WaterBasesFoundationTriangleType,
                _ => false
            };
        }

        private static bool IsWaterBasesFoundation(Dictionary<string, object> entity, string prefab)
        {
            return prefab switch
            {
                WaterBasesFoundationSquarePrefab => GetWaterBasesEntityType(entity) == WaterBasesFoundationSquareType,
                WaterBasesFoundationTrianglePrefab => GetWaterBasesEntityType(entity) == WaterBasesFoundationTriangleType,
                _ => false
            };
        }

        private static bool IsWaterBasesBarrel(BaseEntity entity)
        {
            return entity is CollectibleEntity && entity.ShortPrefabName == "diesel_collectable" && GetWaterBasesEntityType(entity) == WaterBasesBarrelType;
        }

        private static bool IsWaterBasesBarrel(Dictionary<string, object> entity, string prefab)
        {
            return prefab.Equals(WaterBasesDieselPrefab, StringComparison.OrdinalIgnoreCase) && GetWaterBasesEntityType(entity) == WaterBasesBarrelType;
        }

        private static bool IsPrefabFoundation(Dictionary<string, object> entity, string prefab, out bool isWaterFoundation)
        {
            isWaterFoundation = IsWaterBasesFoundation(entity, prefab);
            return isWaterFoundation || prefab.Contains("/foundation/") || prefab.Contains("/foundation.triangle");
        }

        private bool IsImportantPasteEntity(NpcSettings opt, string prefab)
        {
            if (opt != null && opt.Enabled && opt.SpawnAmountScientists > 0 && opt.Inside.Any)
            {
                for (int i = 0; i < NpcImportantPastePrefabs.Length; i++)
                {
                    if (prefab.IndexOf(NpcImportantPastePrefabs[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            for (int i = 0; i < ImportantPastePrefabs.Length; i++)
            {
                if (prefab.IndexOf(ImportantPastePrefabs[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            prefab = GetFileNameWithoutExtension(prefab);
            return IsBox(prefab, true);
        }

        private static bool IsPrefabStair(string prefab) => prefab.EndsWith("/foundation.steps.prefab", StringComparison.OrdinalIgnoreCase);

        private static bool IsPrefabRamp(string prefab) => prefab.EndsWith("ramp.prefab", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetPasteVector3(Dictionary<string, object> entity, string key, out Vector3 value)
        {
            value = default;

            if (!entity.TryGetValue(key, out var obj) || obj is not Dictionary<string, object> axes || !axes.TryGetValue("x", out var x) || !axes.TryGetValue("y", out var y) || !axes.TryGetValue("z", out var z))
            {
                return false;
            }

            value = new Vector3(Convert.ToSingle(x), Convert.ToSingle(y), Convert.ToSingle(z));
            return true;
        }

        private static Quaternion GetPasteRotation(Dictionary<string, object> entity)
        {
            return TryGetPasteVector3(entity, "rot", out var rotation) ? Quaternion.Euler(rotation * Mathf.Rad2Deg) : Quaternion.identity;
        }

        private struct FoundationCornerGroup
        {
            internal int Start;
            internal int Count;
            internal float ContactY;
            internal float ContactClearance;

            internal FoundationCornerGroup(int start, int count, float contactY, float contactClearance)
            {
                Start = start;
                Count = count;
                ContactY = contactY;
                ContactClearance = contactClearance;
            }
        }

        private static FoundationCornerGroup AddFoundationCorners(Dictionary<string, object> entity, string prefab, Vector3 position, Quaternion rotation, bool isWaterFoundation, List<Vector3> corners)
        {
            int start = corners.Count;
            float contactClearance = isWaterFoundation ? GetWaterFoundationContactClearance(entity, rotation) : FoundationContactClearance;

            if (prefab.EndsWith("triangle.prefab", StringComparison.OrdinalIgnoreCase))
            {
                corners.Add(position + rotation * Vector3.forward * FoundationTriangleTipOffset);
                corners.Add(position + rotation * Vector3.left * FoundationCornerOffset);
                corners.Add(position + rotation * Vector3.right * FoundationCornerOffset);
            }
            else
            {
                Vector3 forward = position + rotation * Vector3.forward * FoundationCornerOffset;
                Vector3 back = position + rotation * Vector3.back * FoundationCornerOffset;

                corners.Add(forward + rotation * Vector3.left * FoundationCornerOffset);
                corners.Add(forward + rotation * Vector3.right * FoundationCornerOffset);
                corners.Add(back + rotation * Vector3.right * FoundationCornerOffset);
                corners.Add(back + rotation * Vector3.left * FoundationCornerOffset);
            }

            int count = corners.Count - start;
            float contactY = corners[start].y;

            for (int i = start + 1; i < corners.Count; i++)
            {
                if (corners[i].y < contactY)
                {
                    contactY = corners[i].y;
                }
            }

            return new(start, count, contactY, contactClearance);
        }

        private static float GetWaterBasesBarrelContactClearance(Quaternion rotation)
        {
            // diesel_collectable prefab-local bounds: center (0.05, 0.15, -0.02), extents (0.66, 0.45, 0.51)
            Vector3 center = new(0.05f, 0.15f, -0.02f);
            Vector3 extents = new(0.66f, 0.45f, 0.51f);
            Vector3 rotatedCenter = rotation * center;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            float verticalExtent = Mathf.Abs(right.y) * extents.x + Mathf.Abs(up.y) * extents.y + Mathf.Abs(forward.y) * extents.z;
            return FoundationSurfaceClearance - (rotatedCenter.y - verticalExtent);
        }

        private static float GetWaterFoundationContactClearance(Dictionary<string, object> entity, Quaternion parentRotation)
        {
            float contactClearance = FloorContactClearance;

            if (!entity.TryGetValue("children", out var obj) || obj is not List<object> children)
            {
                return contactClearance;
            }

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not Dictionary<string, object> child || !child.TryGetValue("prefabname", out obj))
                {
                    continue;
                }

                string prefab = obj?.ToString() ?? string.Empty;
                if (!IsWaterBasesBarrel(child, prefab) || !TryGetPasteVector3(child, "pos", out var localPosition) || !TryGetPasteVector3(child, "rot", out var localEuler))
                {
                    continue;
                }

                // Top-level rotations are radians; child rotations are parent-local degrees in CopyPaste data.
                Vector3 childOffset = parentRotation * localPosition;
                Quaternion childRotation = parentRotation * Quaternion.Euler(localEuler);
                float childClearance = GetWaterBasesBarrelContactClearance(childRotation) - childOffset.y;

                if (childClearance > contactClearance)
                {
                    contactClearance = childClearance;
                }
            }

            return contactClearance;
        }

        private static float NormalizeFoundationContactClearances(List<Vector3> corners, List<FoundationCornerGroup> groups)
        {
            float maxClearance = 0f;

            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].ContactClearance > maxClearance)
                {
                    maxClearance = groups[i].ContactClearance;
                }
            }

            if (maxClearance <= 0f)
            {
                return FoundationContactClearance;
            }

            // The resolver stores one clearance for the whole paste. Encode each group's difference into its sample Y while ContactY retains the original level used for multi-level grouping.
            for (int i = 0; i < groups.Count; i++)
            {
                FoundationCornerGroup group = groups[i];
                float offset = maxClearance - group.ContactClearance;

                if (offset <= 0.0001f)
                {
                    continue;
                }

                int end = group.Start + group.Count;
                for (int j = group.Start; j < end; j++)
                {
                    Vector3 point = corners[j];
                    point.y += offset;
                    corners[j] = point;
                }
            }

            return maxClearance;
        }

        private bool IsPrefabExternalWall(Dictionary<string, object> entity)
        {
            string prefabname = entity["prefabname"].ToString();
            return prefabname.Contains("/wall.external.high.") || prefabname.Contains("/gates.external.high.");
        }

        private bool IsPrefabFloor(Dictionary<string, object> entity)
        {
            return entity.TryGetValue("prefabname", out var obj) && obj != null && obj.ToString().Contains("/floor");
        }

        private IEnumerator SetupCopyPasteObstructionRadius()
        {
            using var profiles = Buildings.Profiles.ToPooledList();

            foreach (var profile in profiles)
            {
                var radius = profile.Value.Options.ProtectionRadii.Obstruction == -1 ? 0f : GetObstructionRadius(profile.Value.Options.ProtectionRadii, RaidableType.None);
                foreach (var extra in profile.Value.Options.AdditionalBases)
                {
                    if (!Buildings.Removed.Contains(extra.Key))
                    {
                        yield return SetupCopyPasteObstructionRadius(profile.Value.Options.NPC, extra.Key, radius);
                    }
                }
                if (!Buildings.Removed.Contains(profile.Key))
                {
                    yield return SetupCopyPasteObstructionRadius(profile.Value.Options.NPC, profile.Key, radius);
                }
            }

            setupCopyPasteObstructionRadius = null;
        }

        private IEnumerator SetupCopyPasteObstructionRadius(NpcSettings npcSettings, string baseName, float radius)
        {
            var pasteData = GetPasteData(baseName);
            pasteData.valid = false;

            var filename = Path.Combine("copypaste", baseName);
            if (!HarmonyDataLayer.ExistsDatafile(filename))
            {
                yield break;
            }

            DynamicConfigFile data;
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

            if (data["entities"] is not List<object> entities)
            {
                Queues.Messages.Log(baseName, $"{baseName} is missing entity data");
                Buildings.Remove(baseName);
                yield break;
            }

            using var construct = DisposableList<Vector3>();
            using var allFoundationCorners = DisposableList<Vector3>();
            using var foundationCornerGroups = DisposableList<FoundationCornerGroup>();

            pasteData.ClearPlacementGeometry();

            var floors = pasteData.floors;
            var compound = pasteData.compound;
            var foundations = pasteData.foundations;
            var foundationCorners = pasteData.foundationCorners;
            var importantEntities = pasteData.importantEntities;
            var stairs = pasteData.stairs;
            var ramps = pasteData.ramps;

            FrameDeadline deadline = new(setupFrameBudgetMilliseconds);
            float constructX = 0f;
            float constructZ = 0f;
            float minEntityY = float.MaxValue;
            float maxEntityY = float.MinValue;
            bool hasWaterBasesFoundation = false;

            foreach (var obj in entities)
            {
                if (deadline.Expired)
                {
                    yield return null;
                    deadline.Reset();
                }

                if (obj is not Dictionary<string, object> entity || !entity.TryGetValue("prefabname", out var prefabObj) || !entity.TryGetValue("pos", out var posObj))
                {
                    continue;
                }

                string prefab = prefabObj?.ToString() ?? string.Empty;

                try
                {
                    if (posObj is not Dictionary<string, object> axes)
                    {
                        continue;
                    }

                    var position = new Vector3(Convert.ToSingle(axes["x"]), Convert.ToSingle(axes["y"]), Convert.ToSingle(axes["z"]));

                    if (position.y < minEntityY) minEntityY = position.y;
                    if (position.y > maxEntityY) maxEntityY = position.y;

                    bool isFoundation = IsPrefabFoundation(entity, prefab, out bool isWaterFoundation);
                    bool isFloor = IsPrefabFloor(entity);

                    if (isFoundation)
                    {
                        if (isWaterFoundation) hasWaterBasesFoundation = true;

                        foundations.Add(position);
                        foundationCornerGroups.Add(AddFoundationCorners(entity, prefab, position, GetPasteRotation(entity), isWaterFoundation, allFoundationCorners));
                    }

                    if (isFloor)
                    {
                        floors.Add(position);
                    }

                    if (IsImportantPasteEntity(npcSettings, prefab))
                    {
                        importantEntities.Add(position);
                    }

                    if (IsPrefabStair(prefab))
                    {
                        stairs.Add(position);
                    }
                    else if (IsPrefabRamp(prefab))
                    {
                        ramps.Add(position);
                    }

                    if (isFoundation || isFloor || prefab.Contains("wall"))
                    {
                        compound.Add(position);
                    }

                    if (isFoundation || IsPrefabExternalWall(entity))
                    {
                        construct.Add(position);
                        constructX += position.x;
                        constructZ += position.z;
                    }
                }
                catch (Exception ex)
                {
                    Puts(ex);
                    Puts("Invalid entity found in copypaste file: {0} ({1})", baseName, prefab);
                }
            }

            bool floorsAreFoundations = foundations.Count == 0;
            float foundationClearance = floorsAreFoundations ? FloorContactClearance : NormalizeFoundationContactClearances(allFoundationCorners, foundationCornerGroups);
            float maxGroundedFoundationY = float.MaxValue;
            float dominantFoundationY = 0f;

            if (floorsAreFoundations)
            {
                foundations.AddRange(floors);
                foundationCorners.Clear();

                float minFloorY = float.MaxValue;
                for (int i = 0; i < floors.Count; i++)
                {
                    if (floors[i].y < minFloorY)
                    {
                        minFloorY = floors[i].y;
                    }
                }

                for (int i = 0; i < floors.Count; i++)
                {
                    if (floors[i].y <= minFloorY + 0.1f)
                    {
                        foundationCorners.Add(floors[i]);
                    }
                }

                pasteData.groundedFoundationCornerCount = foundationCorners.Count;
            }
            else
            {
                maxGroundedFoundationY = GetMaxGroundedFoundationY(foundationCornerGroups, out dominantFoundationY);

                if (maxGroundedFoundationY == float.MaxValue)
                {
                    foundationCorners.AddRange(allFoundationCorners);
                    pasteData.groundedFoundationCornerCount = foundationCorners.Count;
                }
                else
                {
                    CopyFoundationCornerGroups(allFoundationCorners, foundationCornerGroups, maxGroundedFoundationY, true, foundationCorners);
                    pasteData.groundedFoundationCornerCount = foundationCorners.Count;
                    CopyFoundationCornerGroups(allFoundationCorners, foundationCornerGroups, maxGroundedFoundationY, false, foundationCorners);
                }
            }

            if (construct.Count == 0)
            {
                foreach (var position in floors)
                {
                    construct.Add(position);
                    constructX += position.x;
                    constructZ += position.z;
                }
            }

            if (foundations.Count == 0 || foundationCorners.Count == 0 || construct.Count == 0)
            {
                Queues.Messages.Log(baseName, $"{baseName} is missing foundation/floor data #1");
                Buildings.Remove(baseName);
                yield break;
            }

            float centerX = 0f;
            float centerZ = 0f;
            float minFoundationY = float.MaxValue;
            float maxFoundationY = float.MinValue;
            List<Vector3> placementFoundations = floorsAreFoundations ? foundationCorners : foundations;

            for (int i = 0; i < placementFoundations.Count; i++)
            {
                Vector3 foundation = placementFoundations[i];
                centerX += foundation.x;
                centerZ += foundation.z;
                if (foundation.y < minFoundationY) minFoundationY = foundation.y;
                if (foundation.y > maxFoundationY) maxFoundationY = foundation.y;
            }

            if (DebugMode && maxGroundedFoundationY != float.MaxValue)
            {
                Queues.Messages.Print($"{baseName} multi-level foundation placement: min={minFoundationY:F2}, max={maxFoundationY:F2}, dominant contact={dominantFoundationY:F2}, terrain-contact max={maxGroundedFoundationY:F2}");
            }

            var constructCenter = new Vector3(constructX / construct.Count, 0f, constructZ / construct.Count);
            constructCenter.y = GetSpawnHeight(constructCenter);

            if (radius == 0f)
            {
                construct.Sort((a, b) => (a - constructCenter).sqrMagnitude.CompareTo((b - constructCenter).sqrMagnitude));
                radius = Vector3.Distance(construct[0], construct[^1]);
            }

            pasteData.FloorsAreFoundations = floorsAreFoundations;
            pasteData.UsesWaterBasesFoundations = hasWaterBasesFoundation;
            pasteData.radius = Mathf.Ceil(Mathf.Max(CELL_SIZE, radius));
            pasteData.foundationClearance = foundationClearance;
            pasteData.minFoundationY = minFoundationY;
            pasteData.maxFoundationY = maxFoundationY;
            pasteData.minEntityY = minEntityY == float.MaxValue ? 0f : minEntityY;
            pasteData.maxEntityY = maxEntityY == float.MinValue ? 0f : maxEntityY;
            pasteData.centerOffset = new Vector3(centerX / placementFoundations.Count, 0f, centerZ / placementFoundations.Count);
            pasteData.valid = true;
        }

        private static void CopyFoundationCornerGroups(List<Vector3> source, List<FoundationCornerGroup> groups, float maxGroundedY, bool grounded, List<Vector3> destination)
        {
            int maxGroundedLevel = Mathf.RoundToInt(maxGroundedY * FoundationLevelPrecision);

            for (int i = 0; i < groups.Count; i++)
            {
                FoundationCornerGroup group = groups[i];
                bool usesTerrain = Mathf.RoundToInt(group.ContactY * FoundationLevelPrecision) <= maxGroundedLevel;

                if (usesTerrain == grounded)
                {
                    for (int j = 0; j < group.Count; j++)
                    {
                        destination.Add(source[group.Start + j]);
                    }
                }
            }
        }

        private static float GetMaxGroundedFoundationY(List<FoundationCornerGroup> groups, out float dominantFoundationY)
        {
            dominantFoundationY = 0f;

            if (groups.Count < 2)
            {
                return float.MaxValue;
            }

            using var levels = DisposableList<int>();

            for (int i = 0; i < groups.Count; i++)
            {
                levels.Add(Mathf.RoundToInt(groups[i].ContactY * FoundationLevelPrecision));
            }

            levels.Sort();
            int dominantLevel = levels[0];
            int currentLevel = dominantLevel;
            int currentCount = 1;
            int mostFoundations = 0;

            for (int i = 1; i <= levels.Count; i++)
            {
                if (i < levels.Count && levels[i] == currentLevel)
                {
                    currentCount++;
                    continue;
                }

                //Levels are sorted, so retaining the first equal-sized band selects the lower one.
                if (currentCount > mostFoundations)
                {
                    mostFoundations = currentCount;
                    dominantLevel = currentLevel;
                }

                if (i < levels.Count)
                {
                    currentLevel = levels[i];
                    currentCount = 1;
                }
            }

            dominantFoundationY = dominantLevel / FoundationLevelPrecision;
            int maxGroundedLevel = dominantLevel + Mathf.RoundToInt(FloatingFoundationExemptHeight * FoundationLevelPrecision);
            return levels[^1] > maxGroundedLevel ? maxGroundedLevel / FoundationLevelPrecision : float.MaxValue;
        }

        private readonly Dictionary<string, object> _emptyProtocol = new();

        private IEnumerator LoadCopyPasteFile(RandomBase rb)
        {
            try
            {
                yield return LoadCopyPasteFileInternal(rb);
            }
            finally
            {
                loadCoroutines.Remove(rb);
            }
        }

        private IEnumerator LoadCopyPasteFileInternal(RandomBase rb)
        {
            DynamicConfigFile data;

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

            yield return ApplyStartPositionAdjustment(rb);

            if (!rb.pasteData.valid || rb.pasteData.foundations.IsNullOrEmpty())
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

            if (!IsUnloading)
            {
                TryInvokeMethod(() => RFManager.GetListenerSet(1).RemoveWhere(obj => obj == null || !BaseEntityEx.IsValidEntityReference(obj)));

                var raid = OpenEvent(rb);

                if (raid.SpawnLegacyShelter())
                {
                    CreatePastedCallback(raid, rb)();
                    yield break;
                }

                InitializePastePositions(raid, rb);

                if (raid.Type != RaidableType.None)
                {
                    int limit = Mathf.Clamp(raid.Options.Setup.SpawnLimit, 1, 500);
                    yield return raid.RemoveClutter(limit);
                }

                var protocol = data["protocol"] as Dictionary<string, object> ?? _emptyProtocol;

                if (_pasteEngine == null)
                {
                    if (!IsUnloading)
                    {
                        const string error = "The internal paste engine is unavailable.";
                        Queues.Messages.Print($"{rb.BaseName} could not be pasted: {error}");
                        Puts("{0} could not be pasted: {1}", rb.BaseName, error);
                        Puts("\nQueue will resume in 30 seconds to prevent the server from being spammed with errors.");
                        isSpawnerBusyTime = Time.timeAsDouble + 30d;
                        isSpawnerBusy = true;
                    }
                    rb.payments.Refund();
                    raid.Despawn();
                    yield break;
                }

                Queues.Messages.Print($"{rb.BaseName} is pasting at {rb.Position}");
                yield return _pasteEngine.Paste(raid, rb, entities, protocol, CreatePastedCallback(raid, rb), CreateSpawnCallback(raid));
            }
        }

        private static void InitializePastePositions(RaidableBase raid, RandomBase rb)
        {
            PasteData data = rb.pasteData;
            Vector3 offset = rb.Position;

            raid.FloorsAreFoundations = data.FloorsAreFoundations;

            for (int i = 0; i < data.foundations.Count; i++)
            {
                raid.foundations.Add(data.foundations[i] + offset);
            }

            for (int i = 0; i < data.floors.Count; i++)
            {
                raid.floors.Add(data.floors[i] + offset);
            }

            for (int i = 0; i < data.compound.Count; i++)
            {
                Vector3 position = data.compound[i] + offset;

                if (Mathf.Abs(position.y - raid.Location.y) < raid.ProtectionRadius)
                {
                    raid.compound.Add(position);
                }
            }
        }

        public enum PasteErrorMode { Undo = 1, Continue = 2 }

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
                raid.DestroyGroundCheck(e);
                if (e is BaseCombatEntity b)
                {
                    b.spawnDeployableCorpseOnDeath = false;
                }
                if (e.ShortPrefabName == "poweredwaterpurifier.storage" && !e.HasParent())
                {
                    e.DelayedSafeKill();
                    return;
                }
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
                if (!raid.stability && e is BuildingBlock block)
                {
                    block.grounded = true;
                }
                foreach (var slot in _checkSlots)
                {
                    if (e.GetSlot(slot) is BaseEntity ent)
                    {
                        raid.AddEntity(ent);
                        raid.RaidEntities.Add(ent);
                    }
                }
                if (e.net == null)
                {
                    e.net = Net.sv.CreateNetworkable();
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
                raid.AddEntity(e);
                raid.RaidEntities.Add(e);
            });
        }

        private IEnumerator ApplyStartPositionAdjustment(RandomBase rb)
        {
            ParseListedOptions(rb);

            if (rb.precisePlacement)
            {
                yield return CoroutineEx.waitForFixedUpdate;
                yield break;
            }

            if (!rb.pasteData.valid)
            {
                yield return SetupCopyPasteObstructionRadius(rb.options.NPC, rb.BaseName, rb.options.ProtectionRadii.Obstruction == -1 ? 0f : GetObstructionRadius(rb.options.ProtectionRadii, RaidableType.None));
            }

            if (!rb.pasteData.valid || rb.pasteData.foundations.IsNullOrEmpty())
            {
                Queues.Messages.Log(rb.BaseName, $"{rb.BaseName} is missing foundation/floor data #3");
                yield break;
            }

            // Precise and fallback placement use the cached center
            rb.Position.x -= rb.pasteData.centerOffset.x;
            rb.Position.z -= rb.pasteData.centerOffset.z;

            if (rb.options.Setup.ForcedHeight != -1f)
            {
                rb.Position.y = rb.baseHeight + rb.options.Setup.PasteHeightAdjustment + rb.options.Setup.ForcedHeight;
            }
            else if (rb.options.Setup.Sky && !rb.isCustomSpawn)
            {
                ApplySkyStartPositionAdjustment(rb);
            }
            else
            {
                float height = rb.baseHeight;

                if (rb.IsWaterSpawn && rb.options.Water.Surface)
                {
                    rb.Position.y = Mathf.Max(WaterSystem.OceanLevel, TerrainMeta.WaterMap.GetHeight(rb.Position));

                    if (rb.pasteData.UsesWaterBasesFoundations)
                    {
                        // WaterBases floors are saved near the paste origin. Align the floor plane to the water surface; barrel depth is only used for terrain and seabed contact.
                        height += -rb.pasteData.minFoundationY - 1f;
                    }
                }
                else if (!rb.isCustomSpawn)
                {
                    rb.Position.y = GetSpawnHeight(rb.Position, !rb.IsWaterSpawn);
                }

                rb.Position.y += height + rb.options.Setup.PasteHeightAdjustment;
            }

            yield return CoroutineEx.waitForFixedUpdate;
        }

        private void ApplySkyStartPositionAdjustment(RandomBase rb)
        {
            float maxGroundY = float.MinValue, terrainY = float.MinValue;
            float raycastY = rb.Position.y;

            foreach (var foundation in rb.pasteData.foundations)
            {
                var position = foundation + rb.Position;
                //Local foundation Y must not move the ray origin below an elevated selected surface.
                position.y = raycastY;
                terrainY = Mathf.Max(terrainY, TerrainMeta.HeightMap.GetHeight(position));
                maxGroundY = Mathf.Max(maxGroundY, GetSpawnHeight(position));
            }

            var desiredHeight = Mathf.Max(0f, rb.baseHeight + rb.options.Setup.PasteHeightAdjustment);
            var clearance = Mathf.Clamp(desiredHeight, 0f, 10f);
            var minBaseY = Mathf.Min(rb.pasteData.minFoundationY, rb.pasteData.minEntityY + desiredHeight - clearance);
            var groundY = Mathf.Max(terrainY, WaterSystem.OceanLevel);
            var targetY = Mathf.Max(groundY + desiredHeight, maxGroundY + clearance);
            var baseY = targetY - rb.pasteData.minFoundationY;
            var raisedY = targetY - minBaseY;
            //Flying vehicles full lift power ceiling less VehicleCeilingMargin so top of base is easily reachable.
            var powerLossY = Mathf.Max(terrainY, HotAirBalloon.minimumAltitudeTerrain) + HotAirBalloon.serviceCeiling - LiftFalloff;
            var maxY = powerLossY - VehicleCeilingMargin - rb.pasteData.maxEntityY;

            //The ceiling limits only the extra safety raise; it never lowers the main base below its configured height.
            rb.Position.y = Mathf.Max(baseY, Mathf.Min(raisedY, maxY));

            var entityDrop = rb.pasteData.minFoundationY - rb.pasteData.minEntityY;
            var wantedRaise = rb.pasteData.minFoundationY - minBaseY;
            var raisedBy = rb.Position.y - baseY;
            var cappedRaiseDiff = wantedRaise - raisedBy;

            if (cappedRaiseDiff > 0.01f) Puts($"{rb.BaseName} has entities {entityDrop:F1}m below the main base. Base has been raised by {raisedBy:F1}m over its configured height, but some entities may still be underground.");
            else if (raisedBy > 10f) Puts($"{rb.BaseName} has entities {entityDrop:F1}m below the main base. Base has been raised by {raisedBy:F1}m over its configured height for these to be above ground.");
            if (DebugMode) Queues.Messages.Print($"{rb.BaseName} sky placement: terrain={terrainY:F2}, ground={maxGroundY:F2}, minEntityY={rb.pasteData.minEntityY:F2}, maxEntityY={rb.pasteData.maxEntityY:F2}, minFloorY={rb.pasteData.minFoundationY:F2}, minBaseY={minBaseY:F2}, desiredHeight={desiredHeight:F2}, baseY={baseY:F2}, raisedY={raisedY:F2}, maxY={maxY:F2}, final={rb.Position.y:F2}");
        }

        [HookMethod("GetSpawnHeight")]
        public float GetSpawnHeight(Vector3 a, bool max = true, bool shouldSkipSmallRock = false, int mask = targetMask, BasePlayer player = null) =>
            SpawnsController.GetSpawnHeight(a, max, shouldSkipSmallRock, mask, player);

        private void ParseListedOptions(RandomBase rb)
        {
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
            else if (type is RaidableType.Maintained or RaidableType.Scheduled)
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
                        SendNotification(owner, "Profile Not Enabled", baseName);
                    }
                    else
                    {
                        SendNotification(owner, "Difficulty Not Buyable", mode);

                        if (blockedPurchasePVP && owner != null && owner.IsAdmin)
                        {
                            SendNotification(owner, "'Allow Players To Buy PVP Raids' is preventing you from buying this PVP raid. Set 'Allow PVP' to 'false' in the PROFILE to fix this.");
                        }
                    }

                    payments.Refund();
                }
                else if (user != null)
                {
                    ReplyOrLog(user, Queues.Messages.GetLast());
                }
            }
            else if (payments != null && spawns == null)
            {
                SendNotification(owner, "CannotFindPosition");
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
            rb.user = user;
            rb.typeDistance = GetDistance(rb.type);
            rb.protectionRadius = rb.options.ProtectionRadius(rb.type);
            rb.safeRadius = Mathf.Max(rb.options.ArenaWalls.Radius, rb.protectionRadius);
            rb.buildRadius = Mathf.Max(config.Settings.Management.CupboardDetectionRadius, rb.options.ArenaWalls.Radius, rb.protectionRadius) + 5f;

            if (owner != null)
            {
                if (owner.clanId != 0) rb.clan = GetClan(owner);
                if (!rb.payments.admin) rb.payments.admin = owner.IsAdmin;
                rb.owner = owner;
                rb.id = owner.UserIDString;
                rb.username = owner.displayName;
                rb.userid = owner.userID;
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

                foreach (var extra in profile.Options.AdditionalBases.Keys)
                {
                    if (FileExists(extra) && (extra == baseName || data.Cycle.CanSpawn(type, mode, extra, player)))
                    {
                        if (!profile.Options.Enabled && extra != baseName)
                        {
                            continue;
                        }

                        if (isBaseNull)
                        {
                            profiles.Add((extra, profile));
                        }
                        else if (extra.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                        {
                            return (extra, profile);
                        }
                    }
                }
            }

            if (profiles.Count > 0)
            {
                return profiles.GetSecureRandom();
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

        protected bool BuildingNotAllowed(string message, object value)
        {
            Queues.Messages.Add(message, value);
            return false;
        }

        private bool IsBuildingAllowed(RaidableType type, string search, BuildingOptions options) => (search == RaidableMode.Random || search == options.Mode) && type switch
        {
            _ when !IsDifficultyEnabledAfterWipe(options.Mode, type, string.Empty, out _) => (BuildingNotAllowed("Cannot spawn difficulty yet", options.Mode), false).Item2,
            RaidableType.Purchased when !CanSpawnDifficultyToday(type, options.Mode) => (BuildingNotAllowed("Cannot spawn difficulty today", options.Mode), false).Item2,
            RaidableType.Purchased when !AllowBuyingPVP && options.AllowPVP => (BuildingNotAllowed("Buyable Events is configured to block PVP purchases.", options.Mode), false).Item2,
            RaidableType.Maintained or RaidableType.Scheduled when !CanSpawnDifficultyToday(type, options.Mode) => (BuildingNotAllowed("Cannot spawn difficulty today", options.Mode), false).Item2,
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
