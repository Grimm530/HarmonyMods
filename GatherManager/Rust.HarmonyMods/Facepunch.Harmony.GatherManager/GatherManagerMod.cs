using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    public class GatherManagerMod : IHarmonyModHooks
    {
        public static GatherManagerMod Instance { get; private set; }

        internal bool DebugGatherEnabled => _config?.DebugGather == true;

        private const float DefaultGatherScale = 2f;
        private float _globalScale = DefaultGatherScale;
        private float _craftScale = 1f;
        private bool _unlockAllBps = false;

        private GatherManagerConfig _config;
        private Dictionary<string, ItemDefinition> _validResources;
        private static readonly Dictionary<string, ResourceDispenser.GatherType> _validDispensers = new Dictionary<string, ResourceDispenser.GatherType>(StringComparer.OrdinalIgnoreCase)
        {
            ["tree"] = ResourceDispenser.GatherType.Tree,
            ["ore"] = ResourceDispenser.GatherType.Ore,
            ["corpse"] = ResourceDispenser.GatherType.Flesh,
            ["flesh"] = ResourceDispenser.GatherType.Flesh
        };
        private bool _serverDataInitialized;
        private readonly object _initLock = new object();

        private const float DefaultMiningQuarryTickRate = 5f;
        private const float DefaultExcavatorTickRate = 3f;

        public float GetMiningQuarryTickRate() => _config?.MiningQuarryResourceTickRate ?? DefaultMiningQuarryTickRate;

        private void EnsureServerData()
        {
            if (_serverDataInitialized) return;
            lock (_initLock)
            {
                if (_serverDataInitialized) return;
                try
                {
                    if (ItemManager.itemList != null)
                    {
                        _validResources = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
                        foreach (var def in ItemManager.itemList)
                        {
                            if (def.category == ItemCategory.Food || def.category == ItemCategory.Resources)
                                _validResources[def.displayName.english.ToLower()] = def;
                        }
                        _serverDataInitialized = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GatherManager] EnsureServerData: {ex.Message}");
                }
            }
        }

        private float GetResourceModifier(Dictionary<string, float> dict, string resourceName)
        {
            if (dict == null || dict.Count == 0) return DefaultGatherScale;
            if (dict.TryGetValue(resourceName, out var v)) return v;
            if (dict.TryGetValue("*", out v)) return v;
            // Case-insensitive fallback (e.g. "Wood" vs "wood")
            foreach (var kv in dict)
            {
                if (string.Equals(kv.Key, resourceName, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return DefaultGatherScale;
        }

        /// <summary>Scale for node gathering (trees, ore, flesh). Uses per-resource config, or global scale when config is empty.</summary>
        internal float GetResourceModifierForDispenser(ResourceDispenser dispenser, string resourceName)
        {
            var dict = _config?.GatherResourceModifiers;
            if (dict == null || dict.Count == 0)
                return _globalScale;
            return GetResourceModifier(dict, resourceName);
        }

        /// <summary>Scale for pickups (hemp, mushrooms, etc.) and random dispenser drops (e.g. seeds). Uses per-resource config or global scale when empty.</summary>
        internal float GetResourceModifierForPickup(string resourceName)
        {
            var dict = _config?.PickupResourceModifiers;
            if (dict == null || dict.Count == 0)
                return _globalScale;
            return GetResourceModifier(dict, resourceName);
        }

        public void OnGatherItem(OnGatherItemArgs args)
        {
            if (args.GivenItem?.info == null) return;

            var itemName = args.GivenItem.info.displayName.english;
            var amtBefore = args.GivenItem.amount;
            float scale = _globalScale;

            switch (args.Source)
            {
                case GatherSource.Dispenser:
                    scale = GetResourceModifier(_config?.GatherResourceModifiers, itemName);
                    break;
                case GatherSource.Growable:
                    scale = GetResourceModifier(_config?.GatherResourceModifiers, itemName);
                    break;
                case GatherSource.Pickup:
                    scale = GetResourceModifier(_config?.PickupResourceModifiers, itemName);
                    break;
                default:
                    scale = _globalScale;
                    break;
            }

            if (Math.Abs(scale - 1f) > 0.001f)
                args.GivenItem.amount = (int)(args.GivenItem.amount * scale);

            if (_config?.DebugGather == true)
                Debug.Log($"[GatherManager] OnGatherItem Source={args.Source} Item={itemName} before={amtBefore} scale={scale} after={args.GivenItem.amount}");
        }

        public void ApplyQuarryModifier(MiningQuarry quarry, Item item)
        {
            if (item?.info == null) return;
            var dict = _config?.QuarryResourceModifiers;
            float scale = (dict == null || dict.Count == 0) ? _globalScale : GetResourceModifier(dict, item.info.displayName.english);
            if (Math.Abs(scale - 1f) > 0.001f)
                item.amount = (int)(item.amount * scale);
        }

        public void ApplyExcavatorModifier(ExcavatorArm excavator, Item item)
        {
            if (item?.info == null) return;
            var dict = _config?.ExcavatorResourceModifiers;
            float scale = (dict == null || dict.Count == 0) ? _globalScale : GetResourceModifier(dict, item.info.displayName.english);
            if (Math.Abs(scale - 1f) > 0.001f)
                item.amount = (int)(item.amount * scale);
        }

        public void ApplySurveyModifier(SurveyCharge surveyCharge, Item item)
        {
            if (item?.info == null) return;
            var dict = _config?.SurveyResourceModifiers;
            float scale = (dict == null || dict.Count == 0) ? _globalScale : GetResourceModifier(dict, item.info.displayName.english);
            if (Math.Abs(scale - 1f) > 0.001f)
                item.amount = (int)(item.amount * scale);
        }

        /// <summary>Oxide-style: modify collectible.itemList before DoPickup gives items. Mirrors OnCollectiblePickup. Uses global scale when PickupResourceModifiers is empty.</summary>
        public void ApplyPickupModifiersToCollectible(CollectibleEntity collectible)
        {
            if (collectible?.itemList == null || collectible.itemList.Length == 0)
            {
                if (_config?.DebugGather == true) Debug.Log("[GatherManager] ApplyPickupModifiersToCollectible SKIP: itemList null/empty");
                return;
            }
            var dict = _config?.PickupResourceModifiers;
            bool useGlobal = dict == null || dict.Count == 0;
            float defaultScale = useGlobal ? _globalScale : 0f;

            if (_config?.DebugGather == true) Debug.Log($"[GatherManager] ApplyPickupModifiersToCollectible RUNNING prefab={collectible.ShortPrefabName} itemList.Count={collectible.itemList.Length} useGlobal={useGlobal} scale={defaultScale}");

            for (int i = 0; i < collectible.itemList.Length; i++)
            {
                var itemAmt = collectible.itemList[i];
                if (itemAmt.itemDef == null) continue;
                var before = itemAmt.amount;
                var scale = useGlobal ? defaultScale : GetResourceModifier(dict, itemAmt.itemDef.displayName.english);
                if (Math.Abs(scale - 1f) > 0.001f)
                {
                    itemAmt.amount = (int)(itemAmt.amount * scale);
                    collectible.itemList[i] = itemAmt;
                    if (_config?.DebugGather == true) Debug.Log($"[GatherManager] Collectible itemList[{i}] {itemAmt.itemDef.displayName.english} before={before} scale={scale} after={itemAmt.amount}");
                }
            }
        }

        public void OnLootSpawned(OnLootSpawnedArgs args)
        {
            foreach (var inventory in args.Inventories)
            {
                foreach (var item in inventory.itemList)
                {
                    if (item?.info == null) continue;
                    var scale = _config != null ? GetResourceModifier(_config.GatherResourceModifiers, item.info.displayName.english) : _globalScale;
                    if (Math.Abs(scale - 1f) > 0.001f)
                        item.amount = Mathf.FloorToInt(item.amount * scale);
                }
            }
        }

        public void OnPlayerConnected(OnPlayerConnectedArgs args)
        {
            if (_unlockAllBps && args.Player != null)
                BasePlayerEx.UnlockAll(args.Player.blueprints);
        }

        public void GetCraftDuration(GetCraftDurationArgs args)
        {
            args.CraftDurationScale = _craftScale;
        }

        public bool OnCommand(CommandContext context)
        {
            var split = SplitCommandWithQuotes(context.RawCommand ?? "");
            if (split.Count == 0) return false;

            var command = split[0].ToLowerInvariant();

            // Never intercept chat commands – Oxide plugin chat commands depend on these
            if (command == "chat.say" || command == "chat.teamsay" || command == "chat.localsay")
                return false;

            if (command == "gather")
            {
                OnGatherIngameCommand(context);
                return true;
            }

            // Admin-only commands – must not block unknown commands (e.g. chat.say, oxide/plugin console commands)
            bool isAdminCommand = command == "gather.scale" || command == "craft.scale" || command == "blueprints.grantall"
                || command == "gather.rate" || command == "gather.resources" || command == "gather.dispensers"
                || command == "dispenser.scale" || command == "quarry.tickrate" || command == "excavator.tickrate";

            if (isAdminCommand && !context.IsAdmin())
            {
                context.AddReply("You don't have permission to use this command.");
                return true;
            }

            if (command == "gather.scale")
            {
                if (split.Count < 2) { context.AddReply($"gather.scale: {_globalScale}"); return true; }
                if (!float.TryParse(split[1], out var amount)) { context.AddReply($"{split[1]} is not a valid amount"); return true; }
                amount = Mathf.Clamp(amount, 1, 1000);
                _globalScale = amount;
                new RepopulateLootTask().Start();
                context.AddReply($"gather.scale: {amount}");
                return true;
            }
            if (command == "craft.scale")
            {
                if (split.Count < 2) { context.AddReply($"craft.scale: {_craftScale}"); return true; }
                if (!float.TryParse(split[1], out var amount)) { context.AddReply($"{split[1]} is not a valid amount"); return true; }
                if (amount > 1) { context.AddReply("Value too high! Use decimals: '0.5' = 1/2 craft time"); return true; }
                _craftScale = Mathf.Clamp(amount, 0.01f, 1f);
                context.AddReply($"craft.scale: {_craftScale}");
                return true;
            }
            if (command == "blueprints.grantall")
            {
                if (split.Count < 2) { context.AddReply($"blueprints.grantall: {_unlockAllBps}"); return true; }
                if (bool.TryParse(split[1], out var v)) { _unlockAllBps = v; context.AddReply($"blueprints.grantall: {_unlockAllBps}"); }
                return true;
            }
            if (command == "gather.rate") { CmdGatherRate(context, split); return true; }
            if (command == "gather.resources") { CmdGatherResources(context); return true; }
            if (command == "gather.dispensers") { CmdGatherDispensers(context); return true; }
            if (command == "dispenser.scale") { CmdDispenserScale(context, split); return true; }
            if (command == "quarry.tickrate") { CmdQuarryTickRate(context, split); return true; }
            if (command == "excavator.tickrate") { CmdExcavatorTickRate(context, split); return true; }

            return false;
        }

        private void CmdGatherRate(CommandContext context, List<string> split)
        {
            EnsureServerData();
            if (_validResources == null) { context.AddReply("Server data not ready."); return; }
            if (split.Count < 4) { context.AddReply("Use gather.rate <dispenser|pickup|quarry|excavator|survey> <resource> <multiplier|remove>"); return; }
            var sub = split[1].ToLowerInvariant();
            bool validType = sub == "dispenser" || sub == "pickup" || sub == "quarry" || sub == "excavator" || sub == "survey";
            if (!validType) { context.AddReply("Type must be dispenser, pickup, quarry, excavator, or survey."); return; }
            var resKey = split[2].ToLowerInvariant();
            if (resKey != "*" && !_validResources.ContainsKey(resKey)) { context.AddReply($"{split[2]} is not a valid resource. Check gather.resources."); return; }
            var resourceName = resKey == "*" ? "*" : _validResources[resKey]?.displayName.english ?? "*";
            var remove = split[3].Equals("remove", StringComparison.OrdinalIgnoreCase);
            float modifier = -1;
            if (!remove && !float.TryParse(split[3], out modifier)) { context.AddReply("Invalid modifier. Use a number or 'remove'."); return; }
            if (!remove && modifier <= 0) { context.AddReply("Modifier must be greater than 0."); return; }

            Dictionary<string, float> dict = null;
            string sourceName = "";
            switch (sub)
            {
                case "dispenser": dict = _config.GatherResourceModifiers; sourceName = "Resource Dispensers"; break;
                case "pickup": dict = _config.PickupResourceModifiers; sourceName = "pickups"; break;
                case "quarry": dict = _config.QuarryResourceModifiers; sourceName = "Mining Quarries"; break;
                case "excavator": dict = _config.ExcavatorResourceModifiers; sourceName = "Excavators"; break;
                case "survey": dict = _config.SurveyResourceModifiers; sourceName = "Survey Charges"; break;
            }
            if (dict == null) dict = new Dictionary<string, float>();

            if (remove) { dict.Remove(resourceName); context.AddReply($"Reset {resourceName} from {sourceName}."); }
            else { dict[resourceName] = modifier; context.AddReply($"Set gather rate for {resourceName} to x{modifier} from {sourceName}."); }
            _config.Save();
        }

        private void CmdGatherResources(CommandContext context)
        {
            EnsureServerData();
            if (_validResources == null) { context.AddReply("Server data not ready."); return; }
            var sb = new StringBuilder("Available resources:\r\n");
            var seen = new HashSet<string>();
            foreach (var r in _validResources.Values)
            {
                if (seen.Add(r.displayName.english))
                    sb.AppendLine(r.displayName.english);
            }
            sb.Append("* (For all resources not setup separately)");
            context.AddReply(sb.ToString());
        }

        private void CmdGatherDispensers(CommandContext context)
        {
            context.AddReply("Available dispensers:\r\nTree\r\nOre\r\nFlesh");
        }

        private void CmdDispenserScale(CommandContext context, List<string> split)
        {
            if (split.Count < 3) { context.AddReply("Use dispenser.scale <tree|ore|corpse> <multiplier>"); return; }
            var disp = split[1].ToLowerInvariant();
            if (!_validDispensers?.ContainsKey(disp) ?? true) { context.AddReply($"{split[1]} is not valid. Use tree, ore, or corpse."); return; }
            if (!float.TryParse(split[2], out var mod) || mod <= 0) { context.AddReply("Modifier must be greater than 0."); return; }
            var key = _validDispensers[disp].ToString("G");
            if (_config.GatherDispenserModifiers == null) _config.GatherDispenserModifiers = new Dictionary<string, float>();
            _config.GatherDispenserModifiers[key] = mod;
            _config.Save();
            context.AddReply($"Set {key} dispensers to x{mod}");
        }

        private void CmdQuarryTickRate(CommandContext context, List<string> split)
        {
            if (split.Count < 2) { context.AddReply("Use quarry.tickrate <seconds>"); return; }
            if (!float.TryParse(split[1], out var sec) || sec < 1) { context.AddReply("Minimum 1 second."); return; }
            _config.MiningQuarryResourceTickRate = sec;
            _config.Save();
            ApplyQuarryTickRateToAll(sec);
            context.AddReply($"Mining Quarry tick rate: {sec} seconds.");
        }

        private void CmdExcavatorTickRate(CommandContext context, List<string> split)
        {
            if (split.Count < 2) { context.AddReply("Use excavator.tickrate <seconds>"); return; }
            if (!float.TryParse(split[1], out var sec) || sec < 1) { context.AddReply("Minimum 1 second."); return; }
            _config.ExcavatorResourceTickRate = sec;
            _config.Save();
            ApplyExcavatorTickRateToAll(sec);
            context.AddReply($"Excavator tick rate: {sec} seconds.");
        }

        private void ApplyQuarryTickRateToAll(float rate)
        {
            try
            {
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    if (entity == null || entity.IsDestroyed) continue;
                    var q = entity as MiningQuarry;
                    if (q != null && q.IsOn())
                    {
                        q.CancelInvoke("ProcessResources");
                        q.InvokeRepeating("ProcessResources", rate, rate);
                    }
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void ApplyExcavatorTickRateToAll(float rate)
        {
            try
            {
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    if (entity == null || entity.IsDestroyed) continue;
                    var arm = entity.GetComponent<ExcavatorArm>();
                    if (arm != null && arm.IsOn())
                    {
                        arm.CancelInvoke("ProduceResources");
                        arm.InvokeRepeating("ProduceResources", rate, rate);
                    }
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void OnGatherIngameCommand(CommandContext context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Resources gained from gathering have been scaled to the following:");
            var hasAny = false;
            if (_config?.GatherResourceModifiers != null && _config.GatherResourceModifiers.Count > 0)
            {
                sb.AppendLine("  Resource Dispensers:");
                foreach (var kv in _config.GatherResourceModifiers)
                    sb.AppendLine($"    {kv.Key}: x{kv.Value}");
                hasAny = true;
            }
            if (_config?.PickupResourceModifiers != null && _config.PickupResourceModifiers.Count > 0)
            {
                sb.AppendLine("  pickups:");
                foreach (var kv in _config.PickupResourceModifiers)
                    sb.AppendLine($"    {kv.Key}: x{kv.Value}");
                hasAny = true;
            }
            if (_config?.QuarryResourceModifiers != null && _config.QuarryResourceModifiers.Count > 0)
            {
                sb.AppendLine("  Mining Quarries:");
                foreach (var kv in _config.QuarryResourceModifiers)
                    sb.AppendLine($"    {kv.Key}: x{kv.Value}");
                hasAny = true;
            }
            if (_config?.ExcavatorResourceModifiers != null && _config.ExcavatorResourceModifiers.Count > 0)
            {
                sb.AppendLine("  Excavators:");
                foreach (var kv in _config.ExcavatorResourceModifiers)
                    sb.AppendLine($"    {kv.Key}: x{kv.Value}");
                hasAny = true;
            }
            if (_config?.SurveyResourceModifiers != null && _config.SurveyResourceModifiers.Count > 0)
            {
                sb.AppendLine("  Survey Charges:");
                foreach (var kv in _config.SurveyResourceModifiers)
                    sb.AppendLine($"    {kv.Key}: x{kv.Value}");
                hasAny = true;
            }
            if (!hasAny) sb.AppendLine("  Default values.");
            if (_config != null && Math.Abs(_config.MiningQuarryResourceTickRate - DefaultMiningQuarryTickRate) > 0.01f)
                sb.AppendLine($"Time between Mining Quarry gathers: {_config.MiningQuarryResourceTickRate} second(s).");
            if (_config?.GatherResourceModifiers == null && _config?.GatherDispenserModifiers == null && Math.Abs(_globalScale - 1f) > 0.001f)
                sb.AppendLine($"Global gather rate: {_globalScale}x");

            context.AddReply(sb.ToString());
            if (context.IsAdmin())
                context.AddReply("Admin: gather.rate <type> <resource> <multiplier>, dispenser.scale <tree|ore|corpse> <multiplier>, quarry.tickrate <sec>, excavator.tickrate <sec>");
        }

        public string GetGatherDescription()
        {
            var sb = new StringBuilder();
            if (_config?.GatherResourceModifiers != null && _config.GatherResourceModifiers.Count > 0)
                sb.Append($"Dispensers scaled. ");
            if (_config?.QuarryResourceModifiers != null && _config.QuarryResourceModifiers.Count > 0)
                sb.Append($"Quarry scaled. ");
            if (_globalScale != 1f)
                sb.Append($"Gather: {_globalScale}x");
            if (sb.Length == 0) sb.Append("Gather Rate: 1x");
            return sb.ToString();
        }

        private static List<string> SplitCommandWithQuotes(string input)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return result;
            var current = new StringBuilder();
            var inQuotes = false;
            foreach (char c in input)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if ((c == ' ' || c == '\t') && !inQuotes)
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                }
                else current.Append(c);
            }
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            _config = GatherManagerConfig.Load();
            EnsureServerData();
            ApplyExcavatorSettingsToAll();
            ServerMgr_UpdateServerInformation.AppendGatherDescription( GetGatherDescription() );
            Debug.Log($"[GatherManager] Loaded - full plugin parity (gather.rate, dispenser.scale, quarry.tickrate, excavator.tickrate, /gather) Debug={_config?.DebugGather == true}");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            RestoreExcavatorDefaults();
            Instance = null;
            Debug.Log("[GatherManager] Unloaded");
        }

        private void ApplyExcavatorSettingsToAll()
        {
            if (_config == null) return;
            try
            {
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    if (entity == null || entity.IsDestroyed) continue;
                    var arm = entity.GetComponent<ExcavatorArm>();
                    if (arm == null) continue;
                    if (Math.Abs(_config.ExcavatorResourceTickRate - DefaultExcavatorTickRate) > 0.01f)
                    {
                        if (arm.IsOn())
                        {
                            arm.CancelInvoke("ProduceResources");
                            arm.InvokeRepeating("ProduceResources", _config.ExcavatorResourceTickRate, _config.ExcavatorResourceTickRate);
                        }
                    }
                    if (Math.Abs(_config.ExcavatorBeltSpeedMax - GatherManagerConfig.DefaultExcavatorBeltSpeedMax) > 0.001f)
                        arm.beltSpeedMax = _config.ExcavatorBeltSpeedMax;
                    if (Math.Abs(_config.ExcavatorTimeForFullResources - GatherManagerConfig.DefaultExcavatorTimeForFullResources) > 0.01f)
                        arm.timeForFullResources = _config.ExcavatorTimeForFullResources;
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void RestoreExcavatorDefaults()
        {
            try
            {
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    if (entity == null || entity.IsDestroyed) continue;
                    var arm = entity.GetComponent<ExcavatorArm>();
                    if (arm == null) continue;
                    arm.CancelInvoke("ProduceResources");
                    arm.InvokeRepeating("ProduceResources", DefaultExcavatorTickRate, DefaultExcavatorTickRate);
                    arm.beltSpeedMax = GatherManagerConfig.DefaultExcavatorBeltSpeedMax;
                    arm.timeForFullResources = GatherManagerConfig.DefaultExcavatorTimeForFullResources;
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
