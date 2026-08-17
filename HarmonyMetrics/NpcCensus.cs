using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace HarmonyMetrics;

/// <summary>
/// Classifies live NPCs into vanilla vs mod-spawned.
/// Primary truth: GrimmNPC.Scientists registry + ZombieHorde.Horde.AllHordes membership.
/// Fallback: entity type name CustomScientistNpc / CustomAnimalNpc and skinID markers.
/// </summary>
internal static class NpcCensus
{
    public const ulong GrimmNpcSkinId = 11162132011012UL;
    public const ulong AnimalSpawnSkinId = 11491311214163UL;
    public const string CensusVersion = "registry-v3";

    private static Type _zombieNpcType;
    private static Type _botOwnerType;
    private static PropertyInfo _grimmInstanceProp;
    private static PropertyInfo _grimmScientistsProp;
    private static FieldInfo _hordeAllHordesField;
    private static FieldInfo _zombieNpcField;
    private static bool _typesResolved;
    private static float _nextTypeRetryTime;
    private static float _nextAnimalScanTime;
    private static AnimalSnapshot _cachedAnimals;

    // Reused per census tick to avoid alloc churn on the hot path beyond the sets themselves.
    private static readonly HashSet<ulong> GrimmNetIds = new HashSet<ulong>();
    private static readonly HashSet<BasePlayer> GrimmPlayers = new HashSet<BasePlayer>();
    private static readonly HashSet<BasePlayer> ZombiePlayers = new HashSet<BasePlayer>();
    private static string _grimmKeyTypeSample;
    private static string _grimmValueTypeSample;
    private static int _grimmIdsExtracted;
    private static int _grimmStale;

    public struct BotSnapshot
    {
        public int Total;
        public int Vanilla;
        public int Mod;
        public int Grimm;
        public int Zombie;
        public int GrimmOther;
        public int PersonalNpc;
        public int VanillaTunnel;
        public int VanillaUnderwater;
        public int VanillaBandit;
        public int VanillaScarecrow;
        public int VanillaScientist;
        public int VanillaHuman;
        public int VanillaFrankenstein;
        public int VanillaOther;
        public int RegistryGrimm;
        public int RegistryGrimmLive;
        public int RegistryGrimmStale;
        public int RegistryZombie;
        public int RegistryHordes;
        public int RegistryIdsExtracted;
    }

    public struct AnimalSnapshot
    {
        public int Total;
        public int Vanilla;
        public int Mod;
        public bool Valid;
    }

    public static BotSnapshot CountBots()
    {
        EnsureTypes();
        RefreshRegistries(out var registryGrimm, out var registryGrimmLive, out var registryGrimmStale, out var registryZombie, out var registryHordes);

        var snap = new BotSnapshot
        {
            RegistryGrimm = registryGrimm,
            RegistryGrimmLive = registryGrimmLive,
            RegistryGrimmStale = registryGrimmStale,
            RegistryZombie = registryZombie,
            RegistryHordes = registryHordes,
            RegistryIdsExtracted = _grimmIdsExtracted
        };

        // GrimmNPC assigns Steam-range userIDs for appearance models, so CustomScientistNpc
        // entities are usually NOT in BasePlayer.bots (Facepunch only tracks userID < 10_000_000).
        // Authoritative mod counts come from Grimm/Zombie registries.
        snap.Grimm = registryGrimmLive;
        snap.Zombie = registryZombie;
        snap.GrimmOther = Math.Max(0, registryGrimmLive - registryZombie);

        var bots = BasePlayer.bots;
        if (bots != null)
        {
            snap.Total = bots.Count;
            for (var i = 0; i < bots.Count; i++)
            {
                var bot = bots[i];
                if (bot == null || bot.IsDestroyed)
                {
                    continue;
                }

                if (IsPersonalNpc(bot))
                {
                    snap.PersonalNpc++;
                    continue;
                }

                // Rare: Grimm NPC still present in bots (bot-range userID). Don't double-count as vanilla.
                if (GrimmPlayers.Contains(bot) || (bot.net != null && GrimmNetIds.Contains(bot.net.ID.Value)) || IsGrimmNpc(bot))
                {
                    continue;
                }

                snap.Vanilla++;
                ClassifyVanilla(bot, ref snap);
            }
        }

        snap.Mod = snap.Grimm + snap.PersonalNpc;
        return snap;
    }

    public static AnimalSnapshot CountAnimals(bool force = false)
    {
        var now = Time.realtimeSinceStartup;
        if (!force && _cachedAnimals.Valid && now < _nextAnimalScanTime)
        {
            return _cachedAnimals;
        }

        _nextAnimalScanTime = now + 15f;
        var snap = new AnimalSnapshot { Valid = true };
        var entities = BaseNetworkable.serverEntities;
        if (entities == null)
        {
            _cachedAnimals = snap;
            return snap;
        }

        foreach (BaseNetworkable networkable in entities)
        {
            if (networkable is not BaseAnimalNPC animal || animal.IsDestroyed)
            {
                continue;
            }

            snap.Total++;
            if (animal.skinID == AnimalSpawnSkinId || TypeNameIs(animal.GetType(), "CustomAnimalNpc"))
            {
                snap.Mod++;
            }
            else
            {
                snap.Vanilla++;
            }
        }

        _cachedAnimals = snap;
        return snap;
    }

    private static void RefreshRegistries(out int registryGrimm, out int registryGrimmLive, out int registryGrimmStale, out int registryZombie, out int registryHordes)
    {
        GrimmNetIds.Clear();
        GrimmPlayers.Clear();
        ZombiePlayers.Clear();
        registryGrimm = 0;
        registryGrimmLive = 0;
        registryGrimmStale = 0;
        registryZombie = 0;
        registryHordes = 0;
        _grimmIdsExtracted = 0;
        _grimmStale = 0;
        _grimmKeyTypeSample = null;
        _grimmValueTypeSample = null;

        try
        {
            if (_grimmInstanceProp != null && (_grimmScientistsProp != null || _grimmScientistsField != null))
            {
                var instance = _grimmInstanceProp.GetValue(null, null);
                if (instance != null)
                {
                    object scientistsObj = null;
                    if (_grimmScientistsProp != null)
                    {
                        scientistsObj = _grimmScientistsProp.GetValue(instance, null);
                    }
                    else if (_grimmScientistsField != null)
                    {
                        scientistsObj = _grimmScientistsField.GetValue(instance);
                    }

                    if (scientistsObj is IDictionary scientists)
                    {
                        registryGrimm = scientists.Count;
                        foreach (DictionaryEntry entry in scientists)
                        {
                            if (_grimmKeyTypeSample == null && entry.Key != null)
                            {
                                _grimmKeyTypeSample = entry.Key.GetType().FullName;
                            }

                            if (_grimmValueTypeSample == null && entry.Value != null)
                            {
                                _grimmValueTypeSample = entry.Value.GetType().FullName;
                            }

                            TryAddNetId(entry.Key);

                            var entity = entry.Value as BaseEntity;
                            if ((object)entity == null)
                            {
                                // Cross-assembly / unexpected value type — pull net id via reflection.
                                TryAddNetIdFromEntityObject(entry.Value);
                                continue;
                            }

                            // Unity fake-null + destroyed.
                            if ((UnityEngine.Object)entity == null || entity.IsDestroyed)
                            {
                                _grimmStale++;
                                continue;
                            }

                            registryGrimmLive++;
                            if (entity is BasePlayer player)
                            {
                                GrimmPlayers.Add(player);
                            }

                            if (entity.net != null)
                            {
                                GrimmNetIds.Add(entity.net.ID.Value);
                            }
                        }

                        registryGrimmStale = _grimmStale;
                        _grimmIdsExtracted = GrimmNetIds.Count;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[HarmonyMetrics] NpcCensus Grimm registry read failed: " + ex.Message);
        }

        try
        {
            if (_hordeAllHordesField != null)
            {
                if (_hordeAllHordesField.GetValue(null) is IList hordes)
                {
                    registryHordes = hordes.Count;
                    for (var i = 0; i < hordes.Count; i++)
                    {
                        var horde = hordes[i];
                        if (horde == null)
                        {
                            continue;
                        }

                        var membersField = horde.GetType().GetField("members", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (membersField?.GetValue(horde) is not IList members)
                        {
                            continue;
                        }

                        for (var m = 0; m < members.Count; m++)
                        {
                            var zombie = members[m];
                            if (zombie == null)
                            {
                                continue;
                            }

                            BasePlayer npc = null;
                            if (_zombieNpcField != null)
                            {
                                npc = _zombieNpcField.GetValue(zombie) as BasePlayer;
                            }

                            if (npc == null)
                            {
                                var npcProp = zombie.GetType().GetProperty("Npc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                npc = npcProp?.GetValue(zombie, null) as BasePlayer;
                            }

                            if (npc != null && !npc.IsDestroyed)
                            {
                                ZombiePlayers.Add(npc);
                                GrimmPlayers.Add(npc);
                                if (npc.net != null)
                                {
                                    GrimmNetIds.Add(npc.net.ID.Value);
                                }
                            }
                        }
                    }

                    registryZombie = ZombiePlayers.Count;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[HarmonyMetrics] NpcCensus ZombieHorde registry read failed: " + ex.Message);
        }
    }

    private static void TryAddNetId(object keyOrId)
    {
        if (keyOrId == null)
        {
            return;
        }

        if (keyOrId is ulong ul)
        {
            GrimmNetIds.Add(ul);
            return;
        }

        if (keyOrId is uint ui)
        {
            GrimmNetIds.Add(ui);
            return;
        }

        if (keyOrId is long l && l >= 0)
        {
            GrimmNetIds.Add((ulong)l);
            return;
        }

        var type = keyOrId.GetType();
        var valueProp = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (valueProp != null)
        {
            try
            {
                var raw = valueProp.GetValue(keyOrId, null);
                if (raw is ulong vul)
                {
                    GrimmNetIds.Add(vul);
                    return;
                }

                if (raw != null && ulong.TryParse(raw.ToString(), out var parsedFromValue))
                {
                    GrimmNetIds.Add(parsedFromValue);
                    return;
                }
            }
            catch
            {
                // ignored
            }
        }

        if (ulong.TryParse(keyOrId.ToString(), out var parsed))
        {
            GrimmNetIds.Add(parsed);
        }
    }

    private static void TryAddNetIdFromEntityObject(object entityObj)
    {
        if (entityObj == null)
        {
            return;
        }

        // Unity fake-null
        if (entityObj is UnityEngine.Object uo && uo == null)
        {
            _grimmStale++;
            return;
        }

        try
        {
            var type = entityObj.GetType();
            var destroyedProp = type.GetProperty("IsDestroyed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (destroyedProp != null && destroyedProp.GetValue(entityObj, null) is bool destroyed && destroyed)
            {
                _grimmStale++;
                return;
            }

            var netProp = type.GetProperty("net", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var net = netProp?.GetValue(entityObj, null);
            if (net == null)
            {
                return;
            }

            var idProp = net.GetType().GetProperty("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var idObj = idProp?.GetValue(net, null);
            TryAddNetId(idObj);

            if (entityObj is BasePlayer player && !player.IsDestroyed)
            {
                GrimmPlayers.Add(player);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static void ClassifyVanilla(BasePlayer bot, ref BotSnapshot snap)
    {
        switch (bot)
        {
            case TunnelDweller _:
                snap.VanillaTunnel++;
                return;
            case UnderwaterDweller _:
                snap.VanillaUnderwater++;
                return;
            case BanditGuard _:
                snap.VanillaBandit++;
                return;
            case ScarecrowNPC _:
                snap.VanillaScarecrow++;
                return;
            case FrankensteinPet _:
                snap.VanillaFrankenstein++;
                return;
            case ScientistNPC _:
                snap.VanillaScientist++;
                return;
            case HumanNPC _:
                snap.VanillaHuman++;
                return;
            default:
                snap.VanillaOther++;
                return;
        }
    }

    private static bool IsGrimmNpc(BasePlayer bot)
    {
        if (bot.skinID == GrimmNpcSkinId)
        {
            return true;
        }

        return TypeNameIs(bot.GetType(), "CustomScientistNpc");
    }

    private static bool IsPersonalNpc(BasePlayer bot)
    {
        if (_botOwnerType != null && bot.GetComponent(_botOwnerType) != null)
        {
            return true;
        }

        return HasBehaviourNamed(bot, "BotOwnerComponent");
    }

    private static bool IsZombieHordeMember(BasePlayer bot)
    {
        if (_zombieNpcType != null && bot.GetComponent(_zombieNpcType) != null)
        {
            return true;
        }

        return HasBehaviourNamed(bot, "ZombieNPC");
    }

    private static bool TypeNameIs(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (t == typeof(object) || t == typeof(MonoBehaviour) || t == typeof(Component))
            {
                break;
            }

            if (string.Equals(t.Name, name, StringComparison.Ordinal))
            {
                return true;
            }

            if (t == typeof(BasePlayer) || t == typeof(BaseEntity) || t == typeof(BaseNetworkable))
            {
                break;
            }
        }

        return false;
    }

    private static bool HasBehaviourNamed(BaseEntity entity, string typeName)
    {
        if (entity == null)
        {
            return false;
        }

        var behaviours = entity.GetComponents<MonoBehaviour>();
        for (var i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour != null && string.Equals(behaviour.GetType().Name, typeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureTypes()
    {
        var missing = _zombieNpcType == null
                      || _botOwnerType == null
                      || _grimmInstanceProp == null
                      || _hordeAllHordesField == null;
        if (_typesResolved && !missing)
        {
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (_typesResolved && missing && now < _nextTypeRetryTime)
        {
            return;
        }

        _typesResolved = true;
        _nextTypeRetryTime = now + 30f;

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var asm = assemblies[i];
                if (asm == null)
                {
                    continue;
                }

                var asmName = asm.GetName().Name ?? string.Empty;
                if (IsFrameworkAssembly(asmName))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                for (var t = 0; t < types.Length; t++)
                {
                    var type = types[t];
                    if (type == null)
                    {
                        continue;
                    }

                    var name = type.Name;
                    if (_zombieNpcType == null && name == "ZombieNPC" && typeof(MonoBehaviour).IsAssignableFrom(type))
                    {
                        _zombieNpcType = type;
                        _zombieNpcField = type.GetField("Npc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                            ?? type.GetField("<Npc>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                    }
                    else if (_botOwnerType == null && name == "BotOwnerComponent" && typeof(MonoBehaviour).IsAssignableFrom(type))
                    {
                        _botOwnerType = type;
                    }
                    else if (_grimmInstanceProp == null && name == "GrimmNPC")
                    {
                        _grimmInstanceProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                        _grimmScientistsProp = type.GetProperty("Scientists", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                               ?? type.GetProperty("Scientists", BindingFlags.Instance | BindingFlags.NonPublic);
                        // Scientists may be a private auto-property — try field backing.
                        if (_grimmScientistsProp == null)
                        {
                            var field = type.GetField("<Scientists>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                                        ?? type.GetField("Scientists", BindingFlags.Instance | BindingFlags.NonPublic);
                            if (field != null)
                            {
                                // Wrap field access via a tiny holder using reflection each time — store FieldInfo on scientists prop path.
                                _grimmScientistsField = field;
                            }
                        }
                    }
                    else if (_hordeAllHordesField == null && name == "Horde")
                    {
                        _hordeAllHordesField = type.GetField("AllHordes", BindingFlags.Public | BindingFlags.Static);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[HarmonyMetrics] NpcCensus type resolve failed: " + ex.Message);
        }
    }

    private static FieldInfo _grimmScientistsField;

    private static bool IsFrameworkAssembly(string asmName)
    {
        return asmName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
               || asmName.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)
               || asmName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
               || asmName.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
               || asmName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
               || asmName.StartsWith("Facepunch", StringComparison.OrdinalIgnoreCase)
               || asmName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
               || asmName.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase)
               || asmName.StartsWith("Newtonsoft", StringComparison.OrdinalIgnoreCase)
               || asmName.StartsWith("Mono.", StringComparison.OrdinalIgnoreCase);
    }

    public static void InvalidateTypeCache()
    {
        _typesResolved = false;
        _zombieNpcType = null;
        _botOwnerType = null;
        _grimmInstanceProp = null;
        _grimmScientistsProp = null;
        _grimmScientistsField = null;
        _hordeAllHordesField = null;
        _zombieNpcField = null;
        _nextTypeRetryTime = 0f;
        _cachedAnimals = default;
        GrimmNetIds.Clear();
        GrimmPlayers.Clear();
        ZombiePlayers.Clear();
        _nextTypeRetryTime = 0f;
        _cachedAnimals = default;
        _grimmKeyTypeSample = null;
        _grimmValueTypeSample = null;
        _grimmIdsExtracted = 0;
        _grimmStale = 0;
    }

    public static void AppendStatus(StringBuilder sb)
    {
        var bots = CountBots();
        var animals = CountAnimals(force: true);
        sb.Append("\tNPC census ");
        sb.Append(CensusVersion);
        sb.AppendLine(":");
        sb.Append("\t  facepunch bots=");
        sb.Append(bots.Total);
        sb.Append(" (vanilla=");
        sb.Append(bots.Vanilla);
        sb.Append(") + grimm live=");
        sb.Append(bots.Grimm);
        sb.Append(" => npc_mod=");
        sb.Append(bots.Mod);
        sb.Append(" npc_total≈");
        sb.Append(bots.Vanilla + bots.Mod);
        sb.AppendLine();
        sb.Append("\t  grimm=");
        sb.Append(bots.Grimm);
        sb.Append(" (zombie=");
        sb.Append(bots.Zombie);
        sb.Append(" other=");
        sb.Append(bots.GrimmOther);
        sb.Append(") personalnpc=");
        sb.Append(bots.PersonalNpc);
        sb.AppendLine();
        sb.AppendLine("\t  note: Grimm CustomScientistNpc use Steam-range userIDs so they are excluded from BasePlayer.bots");
        sb.Append("\t  registries: GrimmNPC.Scientists=");
        sb.Append(bots.RegistryGrimm);
        sb.Append(" (live=");
        sb.Append(bots.RegistryGrimmLive);
        sb.Append(" stale=");
        sb.Append(bots.RegistryGrimmStale);
        sb.Append(" ids=");
        sb.Append(bots.RegistryIdsExtracted);
        sb.Append(") ZombieHorde.members=");
        sb.Append(bots.RegistryZombie);
        sb.Append(" hordes=");
        sb.Append(bots.RegistryHordes);
        sb.Append(" (resolved GrimmInst=");
        sb.Append(_grimmInstanceProp != null);
        sb.Append(" Scientists=");
        sb.Append(_grimmScientistsProp != null || _grimmScientistsField != null);
        sb.Append(" AllHordes=");
        sb.Append(_hordeAllHordesField != null);
        sb.Append(')');
        sb.AppendLine();
        if (!string.IsNullOrEmpty(_grimmKeyTypeSample) || !string.IsNullOrEmpty(_grimmValueTypeSample))
        {
            sb.Append("\t  registry sample types: key=");
            sb.Append(_grimmKeyTypeSample ?? "?");
            sb.Append(" value=");
            sb.Append(_grimmValueTypeSample ?? "?");
            sb.AppendLine();
        }
        sb.Append("\t  vanilla detail: tunnel=");
        sb.Append(bots.VanillaTunnel);
        sb.Append(" underwater=");
        sb.Append(bots.VanillaUnderwater);
        sb.Append(" bandit=");
        sb.Append(bots.VanillaBandit);
        sb.Append(" scarecrow=");
        sb.Append(bots.VanillaScarecrow);
        sb.Append(" scientist=");
        sb.Append(bots.VanillaScientist);
        sb.Append(" human=");
        sb.Append(bots.VanillaHuman);
        sb.Append(" frankenstein=");
        sb.Append(bots.VanillaFrankenstein);
        sb.Append(" other=");
        sb.Append(bots.VanillaOther);
        sb.AppendLine();
        sb.Append("\tAnimals: total=");
        sb.Append(animals.Total);
        sb.Append(" vanilla=");
        sb.Append(animals.Vanilla);
        sb.Append(" animalspawn=");
        sb.Append(animals.Mod);
        sb.AppendLine();
        AppendTypeHistogram(sb);
    }

    private static void AppendTypeHistogram(StringBuilder sb)
    {
        var counts = new Dictionary<string, int>(64);

        var bots = BasePlayer.bots;
        if (bots != null)
        {
            var limit = Math.Min(bots.Count, 800);
            for (var i = 0; i < limit; i++)
            {
                var bot = bots[i];
                if (bot == null || bot.IsDestroyed)
                {
                    continue;
                }

                // Skip any Grimm entries that somehow appear in bots — counted from registry below.
                if (GrimmPlayers.Contains(bot) || (bot.net != null && GrimmNetIds.Contains(bot.net.ID.Value)))
                {
                    continue;
                }

                AddHistogramCount(counts, HistogramLabel(bot, grimm: false, zombie: false));
            }
        }

        foreach (var grimm in GrimmPlayers)
        {
            if (grimm == null || grimm.IsDestroyed)
            {
                continue;
            }

            AddHistogramCount(counts, HistogramLabel(grimm, grimm: true, zombie: ZombiePlayers.Contains(grimm)));
        }

        if (counts.Count == 0)
        {
            return;
        }

        sb.AppendLine("\t  NPC type histogram (Facepunch bots + Grimm registry, top 8):");
        for (var rank = 0; rank < 8 && counts.Count > 0; rank++)
        {
            string bestKey = null;
            var bestVal = -1;
            foreach (var pair in counts)
            {
                if (pair.Value > bestVal)
                {
                    bestVal = pair.Value;
                    bestKey = pair.Key;
                }
            }

            if (bestKey == null)
            {
                break;
            }

            sb.Append("\t    ");
            sb.Append(bestVal);
            sb.Append(" x ");
            sb.Append(bestKey);
            sb.AppendLine();
            counts.Remove(bestKey);
        }
    }

    private static void AddHistogramCount(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out var n);
        counts[key] = n + 1;
    }

    private static string HistogramLabel(BasePlayer npc, bool grimm, bool zombie)
    {
        var name = npc.GetType().Name;
        if (zombie)
        {
            return name + " [ZombieHorde]";
        }

        if (grimm)
        {
            return name + " [GrimmNPC]";
        }

        return name;
    }
}
