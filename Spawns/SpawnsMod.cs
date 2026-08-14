using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SpawnsHarmony
{
    public class SpawnsMod : IHarmonyModHooks
    {
        public static SpawnsMod Instance { get; private set; }
        public const string AppDomainApiKey = "Spawns_ApiType";
        public const int VersionMajor = 2;
        public const int VersionMinor = 0;
        public const int VersionPatch = 36;

        private SpawnsData _spawnsData = new SpawnsData();
        private readonly Dictionary<string, List<Vector3>> _loadedSpawnfiles = new Dictionary<string, List<Vector3>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, List<Vector3>> _spawnFileCreators = new Dictionary<ulong, List<Vector3>>();
        private readonly List<ulong> _isEditing = new List<ulong>();
        private readonly Dictionary<string, string> _lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string _dataDir;
        private string _indexPath;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _dataDir = Path.Combine(root, "HarmonyData", "Spawns");
            Directory.CreateDirectory(_dataDir);
            _indexPath = Path.Combine(_dataDir, "spawns_data.json");

            LoadLang(root);
            LoadData();

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(SpawnsMod)); }
            catch { }

            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(VerifyFilesExist, 1f);
            else
            {
                var go = new GameObject("Spawns_InitWait");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<SpawnsInitWait>().Begin(this);
            }

            Debug.Log($"[Spawns] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[Spawns] -> Data: HarmonyData/Spawns/");
            Debug.Log("[Spawns] -> API: AppDomain Spawns_ApiType");
        }

        internal void OnServerReady() => VerifyFilesExist();

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }
            _loadedSpawnfiles.Clear();
            _spawnFileCreators.Clear();
            _isEditing.Clear();
            Instance = null;
            Debug.Log("[Spawns] OK: Unloaded.");
        }

        private void LoadLang(string root)
        {
            foreach (var kv in DefaultMessages)
                _lang[kv.Key] = kv.Value;
            try
            {
                var path = Path.Combine(root, "HarmonyLanguage", "Spawns.json");
                if (!File.Exists(path)) return;
                var extra = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (extra == null) return;
                foreach (var kv in extra)
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _lang[kv.Key] = kv.Value;
            }
            catch (Exception ex) { Debug.LogWarning("[Spawns] Lang load: " + ex.Message); }
        }

        private string Msg(string key) => _lang.TryGetValue(key, out var v) ? v : key;

        private void VerifyFilesExist()
        {
            bool hasChanged = false;
            for (int i = _spawnsData.Spawnfiles.Count - 1; i >= 0; i--)
            {
                string name = _spawnsData.Spawnfiles[i];
                if (!File.Exists(SpawnFilePath(name)))
                {
                    _spawnsData.Spawnfiles.RemoveAt(i);
                    hasChanged = true;
                    continue;
                }
                if (LoadSpawns(name) != null)
                {
                    _spawnsData.Spawnfiles.RemoveAt(i);
                    hasChanged = true;
                }
                else if (_loadedSpawnfiles.TryGetValue(name, out var list) && list.Count == 0)
                {
                    _spawnsData.Spawnfiles.RemoveAt(i);
                    hasChanged = true;
                }
            }
            if (hasChanged) SaveData();
        }

        private object LoadSpawns(string name)
        {
            if (string.IsNullOrEmpty(name))
                return Msg("noFile");

            if (!_loadedSpawnfiles.ContainsKey(name))
            {
                var success = LoadSpawnFile(name);
                if (success == null)
                    return Msg("noFile");
                _loadedSpawnfiles[name] = success;
            }
            return null;
        }

        // ---- Oxide-compatible API (static for AppDomain / ZoneManager) ----

        public static object GetSpawns(string filename)
        {
            var inst = Instance;
            if (inst == null) return "Spawns is not loaded";
            object err = inst.LoadSpawns(filename);
            if (err != null) return err;
            return inst._loadedSpawnfiles[filename];
        }

        public static object GetSpawnsCount(string filename)
        {
            var inst = Instance;
            if (inst == null) return "Spawns is not loaded";
            object err = inst.LoadSpawns(filename);
            if (err != null) return err;
            return inst._loadedSpawnfiles[filename].Count;
        }

        public static object GetRandomSpawn(string filename)
        {
            var inst = Instance;
            if (inst == null) return "Spawns is not loaded";
            object err = inst.LoadSpawns(filename);
            if (err != null) return err;
            var list = inst._loadedSpawnfiles[filename];
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        public static object GetRandomSpawnRange(string filename, int min, int max)
        {
            var inst = Instance;
            if (inst == null) return "Spawns is not loaded";
            object err = inst.LoadSpawns(filename);
            if (err != null) return err;
            var list = inst._loadedSpawnfiles[filename];
            return list[UnityEngine.Random.Range(Mathf.Clamp(min, 0, list.Count - 1), Mathf.Clamp(max, 0, list.Count - 1))];
        }

        public static object GetSpawn(string filename, int number)
        {
            var inst = Instance;
            if (inst == null) return "Spawns is not loaded";
            object err = inst.LoadSpawns(filename);
            if (err != null) return err;
            var list = inst._loadedSpawnfiles[filename];
            return list[Mathf.Clamp(number, 0, list.Count - 1)];
        }

        public static string[] GetSpawnfileNames()
        {
            var inst = Instance;
            if (inst == null) return Array.Empty<string>();
            return inst._spawnsData.Spawnfiles.ToArray();
        }

        public static object Call(string method, params object[] args)
        {
            if (string.IsNullOrEmpty(method)) return null;
            try
            {
                int count = args?.Length ?? 0;
                var mi = typeof(SpawnsMod).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == count);
                return mi?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Spawns] Call(" + method + "): " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || !command.Equals("spawns", StringComparison.OrdinalIgnoreCase))
                return false;
            CmdSpawns(player, args ?? Array.Empty<string>());
            return true;
        }

        private void CmdSpawns(BasePlayer player, string[] args)
        {
            if (player.net?.connection == null || player.net.connection.authLevel < 1)
            {
                player.ChatMessage(Msg("noAccess"));
                return;
            }

            if (args == null || args.Length == 0)
            {
                SendHelpText(player);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "new":
                    if (IsCreatingFile(player))
                    {
                        player.ChatMessage(Msg("alreadyCreating"));
                        return;
                    }
                    _spawnFileCreators[player.userID] = new List<Vector3>();
                    player.ChatMessage(Msg("newCreating"));
                    return;

                case "open":
                    if (args.Length < 2)
                    {
                        player.ChatMessage(Msg("fileName"));
                        return;
                    }
                    if (IsCreatingFile(player))
                    {
                        player.ChatMessage(Msg("isCreating"));
                        return;
                    }
                    var spawns = LoadSpawnFile(args[1]);
                    if (spawns != null)
                    {
                        _spawnFileCreators[player.userID] = spawns;
                        player.ChatMessage(string.Format(Msg("opened"), spawns.Count));
                        if (!_isEditing.Contains(player.userID))
                            _isEditing.Add(player.userID);
                    }
                    else player.ChatMessage(Msg("invalidFile"));
                    return;

                case "add":
                    if (!IsCreatingFile(player))
                    {
                        player.ChatMessage(Msg("notCreating"));
                        return;
                    }
                    _spawnFileCreators[player.userID].Add(player.transform.position);
                    int number = _spawnFileCreators[player.userID].Count;
                    DDrawPosition(player, _spawnFileCreators[player.userID][number - 1], number.ToString());
                    player.ChatMessage($"Added Spawn n°{number}");
                    return;

                case "remove":
                    if (args.Length < 2)
                    {
                        player.ChatMessage("/spawns remove <number>");
                        return;
                    }
                    if (!IsCreatingFile(player))
                    {
                        player.ChatMessage(Msg("notCreating"));
                        return;
                    }
                    if (_spawnFileCreators[player.userID].Count <= 0)
                    {
                        player.ChatMessage(Msg("noSpawnpoints"));
                        return;
                    }
                    if (!int.TryParse(args[1], out int rem))
                    {
                        player.ChatMessage(Msg("noNum"));
                        return;
                    }
                    if (rem <= _spawnFileCreators[player.userID].Count)
                    {
                        _spawnFileCreators[player.userID].RemoveAt(rem - 1);
                        player.ChatMessage(string.Format(Msg("remSuccess"), rem));
                    }
                    else player.ChatMessage(Msg("nexistNum"));
                    return;

                case "save":
                    if (args.Length < 2)
                    {
                        player.ChatMessage("/spawns save <filename>");
                        return;
                    }
                    if (!IsCreatingFile(player))
                    {
                        player.ChatMessage(Msg("noCreate"));
                        return;
                    }
                    if (!_spawnFileCreators.TryGetValue(player.userID, out var pts) || pts.Count == 0)
                    {
                        player.ChatMessage(Msg("noSpawnpoints"));
                        return;
                    }
                    if (!_spawnsData.Spawnfiles.Contains(args[1]) && !_loadedSpawnfiles.ContainsKey(args[1]))
                    {
                        player.ChatMessage(string.Format(Msg("saved"), pts.Count, args[1]));
                        SaveSpawnFile(player, args[1]);
                        return;
                    }
                    if (_isEditing.Contains(player.userID))
                    {
                        SaveSpawnFile(player, args[1]);
                        player.ChatMessage(string.Format(Msg("overwriteSuccess"), args[1]));
                        _isEditing.Remove(player.userID);
                        return;
                    }
                    player.ChatMessage(Msg("spawnfileExists"));
                    return;

                case "close":
                    if (!IsCreatingFile(player))
                    {
                        player.ChatMessage(Msg("noCreate"));
                        return;
                    }
                    _spawnFileCreators.Remove(player.userID);
                    player.ChatMessage(Msg("noSave"));
                    return;

                case "show":
                    if (!IsCreatingFile(player))
                    {
                        player.ChatMessage(Msg("notCreating"));
                        return;
                    }
                    if (_spawnFileCreators[player.userID].Count <= 0)
                    {
                        player.ChatMessage(Msg("noSp"));
                        return;
                    }
                    float time = 10f;
                    if (args.Length > 1)
                        float.TryParse(args[1], out time);
                    for (int i = 0; i < _spawnFileCreators[player.userID].Count; i++)
                        DDrawPosition(player, _spawnFileCreators[player.userID][i], i.ToString(), time);
                    return;

                default:
                    SendHelpText(player);
                    break;
            }
        }

        private static void DDrawPosition(BasePlayer player, Vector3 point, string name, float time = 10f)
        {
            player.SendConsoleCommand("ddraw.text", time, Color.green, point + new Vector3(0, 1.5f, 0), $"<size=40>{name}</size>");
            player.SendConsoleCommand("ddraw.box", time, Color.green, point, 1f);
        }

        private void SendHelpText(BasePlayer player)
        {
            player.ChatMessage(Msg("newSyn"));
            player.ChatMessage(Msg("openSyn"));
            player.ChatMessage(Msg("addSyn"));
            player.ChatMessage(Msg("remSyn"));
            player.ChatMessage(Msg("saveSyn"));
            player.ChatMessage(Msg("closeSyn"));
            player.ChatMessage(Msg("showSyn"));
        }

        private bool IsCreatingFile(BasePlayer player) => _spawnFileCreators.ContainsKey(player.userID);

        private string SpawnFilePath(string name) => Path.Combine(_dataDir, name + ".json");

        private void SaveData()
        {
            try
            {
                File.WriteAllText(_indexPath, JsonConvert.SerializeObject(_spawnsData, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[Spawns] SaveData: " + ex.Message); }
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_indexPath))
                    _spawnsData = JsonConvert.DeserializeObject<SpawnsData>(File.ReadAllText(_indexPath)) ?? new SpawnsData();
                else
                    _spawnsData = new SpawnsData();
            }
            catch
            {
                _spawnsData = new SpawnsData();
            }
        }

        private void SaveSpawnFile(BasePlayer player, string name)
        {
            var spawnFile = new Spawnfile();
            var list = _spawnFileCreators[player.userID];
            for (int i = 0; i < list.Count; i++)
                spawnFile.spawnPoints[i.ToString()] = list[i];

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                Converters = { new StringEnumConverter(), new UnityVector3Converter() }
            };
            File.WriteAllText(SpawnFilePath(name), JsonConvert.SerializeObject(spawnFile, settings));

            if (!_spawnsData.Spawnfiles.Contains(name))
                _spawnsData.Spawnfiles.Add(name);
            _loadedSpawnfiles[name] = new List<Vector3>(list);
            SaveData();
            _spawnFileCreators.Remove(player.userID);
        }

        private List<Vector3> LoadSpawnFile(string name)
        {
            var path = SpawnFilePath(name);
            if (!File.Exists(path)) return null;
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Converters = { new StringEnumConverter(), new UnityVector3Converter() }
                };
                var spawnFile = JsonConvert.DeserializeObject<Spawnfile>(File.ReadAllText(path), settings);
                if (spawnFile?.spawnPoints == null || spawnFile.spawnPoints.Count < 1)
                    return null;
                return spawnFile.spawnPoints.Values.ToList();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Spawns] LoadSpawnFile(" + name + "): " + ex.Message);
                return null;
            }
        }

        private class SpawnsData
        {
            public List<string> Spawnfiles = new List<string>();
        }

        private class Spawnfile
        {
            public Dictionary<string, Vector3> spawnPoints = new Dictionary<string, Vector3>();
        }

        private class UnityVector3Converter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Vector3 vector = (Vector3)value;
                writer.WriteValue($"{vector.x} {vector.y} {vector.z}");
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    string[] values = reader.Value.ToString().Trim().Split(' ');
                    return new Vector3(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2]));
                }
                JObject o = JObject.Load(reader);
                return new Vector3(Convert.ToSingle(o["x"]), Convert.ToSingle(o["y"]), Convert.ToSingle(o["z"]));
            }

            public override bool CanConvert(Type objectType) => objectType == typeof(Vector3);
        }

        private static readonly Dictionary<string, string> DefaultMessages = new Dictionary<string, string>
        {
            {"noFile", "This file doesn't exist" },
            {"alreadyCreating", "You are already creating a spawn file" },
            {"newCreating", "You now creating a new spawn file" },
            {"isCreating", "You must save/close your current spawn file first. Type /spawns for more information" },
            {"opened", "Opened spawnfile with {0} spawns" },
            {"invalidFile", "This spawnfile is empty or not valid" },
            {"fileName", "You must enter a filename" },
            {"notCreating", "You must create/open a new Spawn file first /spawns for more information" },
            {"remSuccess", "Successfully removed spawn n°{0}" },
            {"nexistNum", "This spawn number doesn't exist" },
            {"noNum", "You must enter a spawn point number" },
            {"noSpawnpoints", "You haven't set any spawn points yet" },
            {"noCreate", "You must create a new Spawn file first. Type /spawns for more information" },
            {"noSave", "Spawn file closed without saving" },
            {"noSp", "You must add spawnpoints first" },
            {"newSyn", "/spawns new - Create a new spawn file" },
            {"openSyn", "/spawns open - Open a existing spawn file for editing" },
            {"addSyn", "/spawns add - Add a new spawn point" },
            {"remSyn", "/spawns remove <number> - Remove a spawn point" },
            {"saveSyn", "/spawns save <filename> - Saves your spawn file" },
            {"closeSyn", "/spawns close - Cancel spawn file creation" },
            {"showSyn", "/spawns show <opt:time> - Display a box at each spawnpoint" },
            {"noAccess", "You are not allowed to use this command" },
            {"saved", "{0} spawnpoints saved into {1}" },
            {"spawnfileExists", "A spawn file with that name already exists" },
            {"overwriteSuccess", "You have successfully edited the spawnfile {0}" }
        };
    }

    internal sealed class SpawnsInitWait : MonoBehaviour
    {
        private SpawnsMod _mod;
        public void Begin(SpawnsMod mod)
        {
            _mod = mod;
            StartCoroutine(Wait());
        }
        private System.Collections.IEnumerator Wait()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            yield return new WaitForSeconds(1f);
            _mod?.OnServerReady();
            Destroy(gameObject);
        }
    }
}
